using MediPassPlus.Application.Audit;
using MediPassPlus.Application.Common;
using MediPassPlus.Application.Privacy;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Records;

/// <summary>
/// Implements both history reads. The record is always resolved from a trusted identifier (the verified grant or the
/// authenticated patient), the stored ciphertext is opened only after that resolution succeeds, every read writes a
/// <c>RecordRead</c> audit row before the response is built, and both lists are paged (FR-REC-02, FR-REC-07,
/// FR-PRV-01, FR-PRV-02, NFR-PRF-04, ADR-0004, ADR-0005).
/// </summary>
public sealed class RecordHistoryService : IRecordHistoryService
{
    private readonly IRecordHistoryStore _store;
    private readonly IClinicalTextProtector _protector;
    private readonly IAuditWriter _auditWriter;

    /// <summary>Initializes a new instance of the <see cref="RecordHistoryService"/> class.</summary>
    /// <param name="store">The read side that returns ciphertext rows.</param>
    /// <param name="protector">The only component allowed to decrypt clinical text.</param>
    /// <param name="auditWriter">Writes the <c>RecordRead</c> row before the response is returned.</param>
    public RecordHistoryService(
        IRecordHistoryStore store,
        IClinicalTextProtector protector,
        IAuditWriter auditWriter)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(auditWriter);

        _store = store;
        _protector = protector;
        _auditWriter = auditWriter;
    }

    /// <inheritdoc />
    public async Task<RecordHistoryResult> GetForDoctorAsync(
        int appointmentId,
        AccessGrant verifiedGrant,
        Guid doctorUserId,
        RecordHistoryQuery query,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verifiedGrant);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

        // The filter verified the grant against the database for this appointment. Re-check the scope here so a
        // filter wired to the wrong route fails closed instead of reading another visit's record.
        if (verifiedGrant.AppointmentId != appointmentId)
        {
            return RecordHistoryResult.Denied(RecordHistoryFailure.ScopeMismatch);
        }

        return await ReadAsync(
            verifiedGrant.PatientId,
            doctorUserId,
            verifiedGrant.Id,
            query,
            ipAddress,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<RecordHistoryResult> GetForPatientAsync(
        Guid patientUserId,
        RecordHistoryQuery query,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

        // A self-read needs no grant, so the same identifier is the scope and the audit actor, and the grant column
        // stays null (ADR-0004 decision 1, ADR-0005).
        return ReadAsync(patientUserId, patientUserId, accessGrantId: null, query, ipAddress, cancellationToken);
    }

    private async Task<RecordHistoryResult> ReadAsync(
        Guid patientUserId,
        Guid readerUserId,
        Guid? accessGrantId,
        RecordHistoryQuery query,
        string ipAddress,
        CancellationToken cancellationToken)
    {
        var record = await _store.FindRecordAsync(patientUserId, cancellationToken);
        if (record is null)
        {
            // No record means no medical data was read, so nothing is returned and nothing is audited.
            return RecordHistoryResult.Denied(RecordHistoryFailure.RecordNotFound);
        }

        var diagnoses = await _store.ListDiagnosesAsync(
            record.Id,
            query.GetSkip(),
            query.GetPageSize(),
            cancellationToken);

        IReadOnlyList<PrescriptionHistoryRow> prescriptions = diagnoses.Items.Count == 0
            ? Array.Empty<PrescriptionHistoryRow>()
            : await _store.ListPrescriptionsAsync(
                record.Id,
                diagnoses.Items.Select(row => row.Id).ToList(),
                cancellationToken);

        var reports = await _store.ListReportsAsync(
            record.Id,
            query.GetReportSkip(),
            query.GetReportPageSize(),
            cancellationToken);

        // Audit before any ciphertext is opened: the insert commits on its own for reads, and a read can never reach
        // the client without its row (FR-PRV-02, ADR-0004 decisions 1 and 7).
        await _auditWriter.WriteAsync(
            new AuditEntry(
                readerUserId,
                patientUserId,
                AuditAction.RecordRead,
                AuditEntityType.MedicalRecord,
                record.Id.ToString(),
                ipAddress,
                accessGrantId),
            cancellationToken);

        var prescriptionsByDiagnosis = prescriptions.ToLookup(row => row.DiagnosisId);

        return RecordHistoryResult.Success(new RecordHistoryResponse
        {
            MedicalRecordId = record.Id,
            RecordCreatedAtUtc = record.CreatedAtUtc,
            Diagnoses = new PagedResult<DiagnosisHistoryItemResponse>
            {
                Items = diagnoses.Items
                    .Select(row => ToResponse(row, prescriptionsByDiagnosis[row.Id]))
                    .ToList(),
                TotalCount = diagnoses.TotalCount,
                Page = query.Page,
                PageSize = query.GetPageSize()
            },
            Reports = new PagedResult<ReportMetadataResponse>
            {
                Items = reports.Items.Select(ToResponse).ToList(),
                TotalCount = reports.TotalCount,
                Page = query.ReportPage,
                PageSize = query.GetReportPageSize()
            }
        });
    }

    private DiagnosisHistoryItemResponse ToResponse(
        DiagnosisHistoryRow row,
        IEnumerable<PrescriptionHistoryRow> prescriptions) =>
        new()
        {
            Id = row.Id,
            AppointmentId = row.AppointmentId,
            Description = _protector.Unprotect(ClinicalTextPurpose.Diagnosis, row.DescriptionCiphertext),
            DiagnosedAtUtc = row.DiagnosedAtUtc,
            SupersedesDiagnosisId = row.SupersedesDiagnosisId,
            Prescriptions = prescriptions.Select(ToResponse).ToList()
        };

    private PrescriptionHistoryItemResponse ToResponse(PrescriptionHistoryRow row) =>
        new()
        {
            Id = row.Id,
            MedicineName = _protector.Unprotect(
                ClinicalTextPurpose.Prescription,
                row.MedicineNameCiphertext),
            Dosage = _protector.Unprotect(
                ClinicalTextPurpose.Prescription,
                row.DosageCiphertext),
            Notes = row.NotesCiphertext is null
                ? null
                : _protector.Unprotect(ClinicalTextPurpose.Prescription, row.NotesCiphertext),
            SupersedesPrescriptionId = row.SupersedesPrescriptionId
        };

    private static ReportMetadataResponse ToResponse(ReportMetadataRow row) =>
        new()
        {
            Id = row.Id,
            ContentType = row.ContentType,
            SizeBytes = row.SizeBytes,
            UploadedAtUtc = row.UploadedAtUtc,
            UploadedByUserId = row.UploadedByUserId
        };
}

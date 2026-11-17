using MediPassPlus.Application.Records;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

/// <summary>
/// The EF Core read side of a patient's permanent record. Every query is <c>AsNoTracking</c>, the record is resolved
/// from the patient account, and the pages are paged in SQL so a large record never loads whole (FR-REC-01,
/// FR-REC-02, NFR-PRF-04, ADR-0005).
/// </summary>
public sealed class RecordHistoryStore : IRecordHistoryStore
{
    private readonly MediPassDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="RecordHistoryStore"/> class.</summary>
    /// <param name="context">The application database context.</param>
    public RecordHistoryStore(MediPassDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<MedicalRecordSummary?> FindRecordAsync(
        Guid patientUserId,
        CancellationToken cancellationToken = default) =>
        _context.MedicalRecords
            .AsNoTracking()
            .Where(record => record.PatientId == patientUserId)
            .Select(record => new MedicalRecordSummary(record.Id, record.CreatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<PagedRows<DiagnosisHistoryRow>> ListDiagnosesAsync(
        Guid medicalRecordId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var rows = _context.Diagnoses
            .AsNoTracking()
            .Where(diagnosis => diagnosis.MedicalRecordId == medicalRecordId);

        var totalCount = await rows.CountAsync(cancellationToken);

        // The identifier is a v7 GUID, so the tie-breaker is chronological too and the order is stable.
        var items = await rows
            .OrderByDescending(diagnosis => diagnosis.DiagnosedAtUtc)
            .ThenByDescending(diagnosis => diagnosis.Id)
            .Skip(skip)
            .Take(take)
            .Select(diagnosis => new DiagnosisHistoryRow(
                diagnosis.Id,
                diagnosis.AppointmentId,
                diagnosis.Description,
                diagnosis.DiagnosedAtUtc,
                diagnosis.SupersedesDiagnosisId))
            .ToListAsync(cancellationToken);

        return new PagedRows<DiagnosisHistoryRow>(items, totalCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrescriptionHistoryRow>> ListPrescriptionsAsync(
        Guid medicalRecordId,
        IReadOnlyCollection<Guid> diagnosisIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diagnosisIds);

        if (diagnosisIds.Count == 0)
        {
            return Array.Empty<PrescriptionHistoryRow>();
        }

        var ids = diagnosisIds.ToList();

        // The join and the record filter are part of the query, so a prescription can never be read through a
        // diagnosis that belongs to another patient's record.
        var items = await (
                from prescription in _context.Prescriptions.AsNoTracking()
                join diagnosis in _context.Diagnoses.AsNoTracking()
                    on prescription.DiagnosisId equals diagnosis.Id
                where diagnosis.MedicalRecordId == medicalRecordId
                    && ids.Contains(prescription.DiagnosisId)
                select new PrescriptionHistoryRow(
                    prescription.Id,
                    prescription.DiagnosisId,
                    prescription.MedicineName,
                    prescription.Dosage,
                    prescription.Notes,
                    prescription.SupersedesPrescriptionId))
            .ToListAsync(cancellationToken);

        // Order after materializing: a v7 Guid is time-ordered, while SQL Server's uniqueidentifier ordering uses a
        // different byte order, so ordering in SQL would not match the insertion order on every provider.
        return items
            .OrderBy(row => row.Id)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<PagedRows<ReportMetadataRow>> ListReportsAsync(
        Guid medicalRecordId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var rows = _context.Reports
            .AsNoTracking()
            .Where(report => report.MedicalRecordId == medicalRecordId);

        var totalCount = await rows.CountAsync(cancellationToken);

        var items = await rows
            .OrderByDescending(report => report.UploadedAtUtc)
            .ThenByDescending(report => report.Id)
            .Skip(skip)
            .Take(take)
            .Select(report => new ReportMetadataRow(
                report.Id,
                report.ContentType,
                report.SizeBytes,
                report.UploadedAtUtc,
                report.UploadedByUserId))
            .ToListAsync(cancellationToken);

        return new PagedRows<ReportMetadataRow>(items, totalCount);
    }
}

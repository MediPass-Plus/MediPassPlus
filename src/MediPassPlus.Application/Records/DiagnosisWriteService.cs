using MediPassPlus.Application.Audit;
using MediPassPlus.Application.Privacy;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Records;

namespace MediPassPlus.Application.Records;

/// <summary>
/// Implements the doctor write path for a medical record. The active grant is re-checked against the database on every
/// call, the appointment status is validated against ADR-0005, the clinical text is encrypted before the row is
/// staged, and the medical row and its audit entry are committed together (FR-REC-03, FR-REC-04, NFR-SEC-05,
/// ADR-0004 decision 7).
/// </summary>
public sealed class DiagnosisWriteService : IDiagnosisWriteService
{
    private readonly IAccessGrantChecker _grantChecker;
    private readonly IDiagnosisScopeReader _scopeReader;
    private readonly IDiagnosisWriteStore _store;
    private readonly IClinicalTextProtector _protector;
    private readonly IAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="DiagnosisWriteService"/> class.</summary>
    /// <param name="grantChecker">The database-backed grant check that runs on every write.</param>
    /// <param name="scopeReader">Resolves a stored diagnosis to its appointment and patient.</param>
    /// <param name="store">The unit of work that stages the medical row.</param>
    /// <param name="protector">The only component allowed to encrypt clinical text (MP-50).</param>
    /// <param name="auditWriter">Stages the audit entry in the same unit of work.</param>
    /// <param name="timeProvider">The clock used for the row timestamp.</param>
    public DiagnosisWriteService(
        IAccessGrantChecker grantChecker,
        IDiagnosisScopeReader scopeReader,
        IDiagnosisWriteStore store,
        IClinicalTextProtector protector,
        IAuditWriter auditWriter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(grantChecker);
        ArgumentNullException.ThrowIfNull(scopeReader);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _grantChecker = grantChecker;
        _scopeReader = scopeReader;
        _store = store;
        _protector = protector;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<DiagnosisWriteResult> CreateDiagnosisAsync(
        int appointmentId,
        Guid doctorUserId,
        CreateDiagnosisRequest request,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Description);
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

        var grantResult = await _grantChecker.RequireActiveGrantAsync(
            doctorUserId,
            appointmentId,
            cancellationToken);

        if (!grantResult.IsAllowed)
        {
            return DiagnosisWriteResult.Denied(DiagnosisWriteFailure.GrantNotActive);
        }

        var grant = grantResult.Grant!;

        // The real checker already scopes the grant to the appointment, but re-check it here so a wrong store or a
        // wrong implementation can never write into another visit's record.
        if (grant.AppointmentId != appointmentId)
        {
            return DiagnosisWriteResult.Denied(DiagnosisWriteFailure.ScopeMismatch);
        }

        var context = await _store.LoadForAppointmentAsync(appointmentId, grant.PatientId, cancellationToken);
        if (context is null)
        {
            return DiagnosisWriteResult.Denied(DiagnosisWriteFailure.AppointmentNotFound);
        }

        if (!IsWritable(context.Status))
        {
            return DiagnosisWriteResult.Denied(DiagnosisWriteFailure.AppointmentNotInVisit);
        }

        if (context.MedicalRecordId is null)
        {
            return DiagnosisWriteResult.Denied(DiagnosisWriteFailure.MedicalRecordNotFound);
        }

        // Encrypt before staging: a failure in the protector must leave no row in the unit of work.
        var ciphertext = _protector.Protect(ClinicalTextPurpose.Diagnosis, request.Description);
        var diagnosis = Diagnosis.Create(
            context.MedicalRecordId.Value,
            appointmentId,
            ciphertext,
            _timeProvider.GetUtcNow().UtcDateTime);

        _store.Add(diagnosis);

        _auditWriter.Add(new AuditEntry(
            doctorUserId,
            grant.PatientId,
            AuditAction.DiagnosisCreated,
            AuditEntityType.Diagnosis,
            diagnosis.Id.ToString(),
            ipAddress,
            grant.Id));

        await _store.SaveChangesAsync(cancellationToken);

        return DiagnosisWriteResult.Success(
            new DiagnosisResponse(diagnosis.Id, diagnosis.AppointmentId, diagnosis.DiagnosedAtUtc));
    }

    /// <inheritdoc />
    public async Task<PrescriptionWriteResult> CreatePrescriptionAsync(
        Guid diagnosisId,
        Guid doctorUserId,
        CreatePrescriptionRequest request,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MedicineName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Dosage);
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

        // The diagnosis is the scope anchor: its stored appointment and patient are never taken from the request.
        var scope = await _scopeReader.FindAsync(diagnosisId, cancellationToken);
        if (scope is null)
        {
            return PrescriptionWriteResult.Denied(DiagnosisWriteFailure.ScopeMismatch);
        }

        var grantResult = await _grantChecker.RequireActiveGrantAsync(
            doctorUserId,
            scope.AppointmentId,
            cancellationToken);

        if (!grantResult.IsAllowed)
        {
            return PrescriptionWriteResult.Denied(DiagnosisWriteFailure.GrantNotActive);
        }

        var grant = grantResult.Grant!;

        // The grant, the diagnosis and its appointment must all describe the same visit and patient.
        if (grant.AppointmentId != scope.AppointmentId || grant.PatientId != scope.PatientId)
        {
            return PrescriptionWriteResult.Denied(DiagnosisWriteFailure.ScopeMismatch);
        }

        var context = await _store.LoadForAppointmentAsync(scope.AppointmentId, scope.PatientId, cancellationToken);
        if (context is null)
        {
            return PrescriptionWriteResult.Denied(DiagnosisWriteFailure.AppointmentNotFound);
        }

        if (!IsWritable(context.Status))
        {
            return PrescriptionWriteResult.Denied(DiagnosisWriteFailure.AppointmentNotInVisit);
        }

        var notesCiphertext = string.IsNullOrWhiteSpace(request.Notes)
            ? null
            : _protector.Protect(ClinicalTextPurpose.Prescription, request.Notes);

        var prescription = Prescription.Create(
            diagnosisId,
            _protector.Protect(ClinicalTextPurpose.Prescription, request.MedicineName),
            _protector.Protect(ClinicalTextPurpose.Prescription, request.Dosage),
            notesCiphertext);

        _store.Add(prescription);

        _auditWriter.Add(new AuditEntry(
            doctorUserId,
            scope.PatientId,
            AuditAction.PrescriptionCreated,
            AuditEntityType.Prescription,
            prescription.Id.ToString(),
            ipAddress,
            grant.Id));

        await _store.SaveChangesAsync(cancellationToken);

        return PrescriptionWriteResult.Success(
            new PrescriptionResponse(prescription.Id, prescription.DiagnosisId));
    }

    /// <summary>
    /// A write is allowed while the visit is in progress and during the post-completion grace window. The grant is
    /// already active when this method runs, so <c>Completed</c> only reaches this point inside that window
    /// (FR-ACS-07, ADR-0005).
    /// </summary>
    private static bool IsWritable(AppointmentStatus status) =>
        status is AppointmentStatus.InVisit or AppointmentStatus.Completed;
}

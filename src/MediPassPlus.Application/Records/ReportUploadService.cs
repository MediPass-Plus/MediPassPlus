using MediPassPlus.Application.Audit;
using MediPassPlus.Application.Privacy;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Records;

namespace MediPassPlus.Application.Records;

/// <summary>
/// Implements the report upload for both callers. The order is fixed and each step is a refusal, not an exception: the
/// record is resolved from the database, the caller is authorized for it (a tenant of the record id, never a patient id
/// from the request), the file is validated against the bytes, the file is stored under a random name, and finally the
/// append-only row and its <c>ReportUploaded</c> audit entry commit together. A failed commit deletes the file, so the
/// record and the disk never disagree (FR-REC-05, FR-REC-06, FR-PRV-02, FR-PRV-05, ADR-0004, ADR-0015).
/// </summary>
public sealed class ReportUploadService : IReportUploadService
{
    private readonly IReportStore _store;
    private readonly IAccessGrantChecker _grantChecker;
    private readonly IReportStorage _storage;
    private readonly IAuditWriter _auditWriter;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="ReportUploadService"/> class.</summary>
    /// <param name="store">The unit of work that resolves the record and stages the row.</param>
    /// <param name="grantChecker">The database-backed grant check a doctor's upload must pass.</param>
    /// <param name="storage">The private file store.</param>
    /// <param name="auditWriter">Stages the audit entry in the same unit of work.</param>
    /// <param name="timeProvider">The clock used for the upload timestamp.</param>
    public ReportUploadService(
        IReportStore store,
        IAccessGrantChecker grantChecker,
        IReportStorage storage,
        IAuditWriter auditWriter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(grantChecker);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _grantChecker = grantChecker;
        _storage = storage;
        _auditWriter = auditWriter;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<ReportUploadResult> UploadAsync(
        Guid recordId,
        Guid callerUserId,
        ReportUploaderRole callerRole,
        IUploadedReportFile file,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

        if (recordId == Guid.Empty || callerUserId == Guid.Empty)
        {
            return ReportUploadResult.Denied(ReportUploadFailure.RecordNotFound);
        }

        var scope = await _store.FindRecordAsync(recordId, cancellationToken);

        if (scope is null)
        {
            return ReportUploadResult.Denied(ReportUploadFailure.RecordNotFound);
        }

        var authorization = await AuthorizeAsync(scope, callerUserId, callerRole, cancellationToken);

        if (authorization.Failure != ReportUploadFailure.None)
        {
            return ReportUploadResult.Denied(authorization.Failure);
        }

        var validation = await ValidateAsync(file, cancellationToken);

        if (!validation.IsValid)
        {
            return ReportUploadResult.Denied(MapValidationFailure(validation.Failure));
        }

        StoredReportFile stored;

        try
        {
            await using var content = file.OpenReadStream();
            stored = await _storage.SaveAsync(content, validation.ContentType, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // The validator accepted the declared length and the bytes then disagreed with it. The client's claim was
            // false, so the upload is refused with the size rule instead of surfacing an internal error.
            return ReportUploadResult.Denied(ReportUploadFailure.TooLarge);
        }

        var uploadedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var report = Report.Create(
            scope.RecordId,
            callerUserId,
            stored.StoredFileName,
            validation.ContentType,
            stored.SizeBytes,
            uploadedAtUtc);

        _store.Add(report);

        // The audit entry names the grant that authorized a doctor's upload and stays null for a patient's own upload
        // (ADR-0004 decision 1, ADR-0005).
        _auditWriter.Add(new AuditEntry(
            callerUserId,
            scope.PatientId,
            AuditAction.ReportUploaded,
            AuditEntityType.Report,
            report.Id.ToString(),
            ipAddress,
            authorization.GrantId));

        try
        {
            await _store.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The row and its audit entry are one unit of work. If it does not commit, the file must not survive.
            await _storage.DeleteAsync(stored.StoredFileName, CancellationToken.None);
            throw;
        }

        return ReportUploadResult.Success(new ReportMetadataResponse
        {
            Id = report.Id,
            ContentType = report.ContentType,
            SizeBytes = report.SizeBytes,
            UploadedAtUtc = report.UploadedAtUtc,
            UploadedByUserId = report.UploadedByUserId
        });
    }

    /// <summary>
    /// Decides whether the caller may write to this record. A patient must own it; a doctor must hold an active grant
    /// for its owner, re-checked in the database on this request (FR-PRV-01, FR-REC-10).
    /// </summary>
    private async Task<AuthorizationOutcome> AuthorizeAsync(
        ReportRecordScope scope,
        Guid callerUserId,
        ReportUploaderRole callerRole,
        CancellationToken cancellationToken)
    {
        switch (callerRole)
        {
            case ReportUploaderRole.Patient:
                return scope.PatientId == callerUserId
                    ? new AuthorizationOutcome(ReportUploadFailure.None, null)
                    : new AuthorizationOutcome(ReportUploadFailure.RecordNotFound, null);

            case ReportUploaderRole.Doctor:
                var check = await _grantChecker.RequireActiveGrantForPatientAsync(
                    callerUserId,
                    scope.PatientId,
                    cancellationToken);

                if (!check.IsAllowed)
                {
                    return new AuthorizationOutcome(ReportUploadFailure.GrantNotActive, null);
                }

                var grant = check.Grant
                    ?? throw new InvalidOperationException("An allowed grant result must carry a grant.");

                // Defence in depth: a grant for another patient authorizes nothing, even if the store returned it.
                if (grant.PatientId != scope.PatientId)
                {
                    return new AuthorizationOutcome(ReportUploadFailure.GrantNotActive, null);
                }

                return new AuthorizationOutcome(ReportUploadFailure.None, grant.Id);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(callerRole),
                    callerRole,
                    "Unknown report uploader role.");
        }
    }

    private static async Task<ReportFileValidationResult> ValidateAsync(
        IUploadedReportFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
        {
            return ReportFileValidationResult.Rejected(ReportFileValidationFailure.EmptyFile);
        }

        // The declared type is compared with the leading bytes, so the check does not depend on the client's header.
        var header = new byte[ReportFileValidator.MaxSignatureLength];

        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(
            header,
            header.Length,
            throwOnEndOfStream: false,
            cancellationToken);

        return ReportFileValidator.Validate(file.ContentType, file.Length, header.AsSpan(0, read));
    }

    private static ReportUploadFailure MapValidationFailure(ReportFileValidationFailure failure) =>
        failure switch
        {
            ReportFileValidationFailure.EmptyFile => ReportUploadFailure.EmptyFile,
            ReportFileValidationFailure.TooLarge => ReportUploadFailure.TooLarge,
            ReportFileValidationFailure.UnsupportedContentType => ReportUploadFailure.UnsupportedContentType,
            ReportFileValidationFailure.ContentMismatch => ReportUploadFailure.ContentMismatch,
            ReportFileValidationFailure.None => throw new InvalidOperationException(
                "An invalid validation result must have a failure reason."),
            _ => throw new InvalidOperationException($"Unknown validation failure: {failure}.")
        };

    private readonly record struct AuthorizationOutcome(ReportUploadFailure Failure, Guid? GrantId);
}

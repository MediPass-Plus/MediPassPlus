using System.IO;
using MediPassPlus.Application.Audit;
using MediPassPlus.Application.Privacy;
using MediPassPlus.Domain.Audit;

namespace MediPassPlus.Application.Records;

/// <summary>
/// Implements report download/open for both callers. The report and its owner are resolved from stored rows, the
/// caller is authorized against that owner (a patient against ownership, a doctor against an active grant re-checked in
/// the database on this request), the file is opened only after authorization, and the <c>ReportDownloaded</c> audit row
/// is written before the stream leaves the service. Every refusal that must not reveal existence answers as not found
/// (FR-REC-08, FR-PRV-01, FR-PRV-02, FR-PRV-05, NFR-PRI-01, ADR-0004, ADR-0015).
/// </summary>
public sealed class ReportDownloadService : IReportDownloadService
{
    private readonly IReportStore _store;
    private readonly IAccessGrantChecker _grantChecker;
    private readonly IReportStorage _storage;
    private readonly IAuditWriter _auditWriter;

    /// <summary>Initializes a new instance of the <see cref="ReportDownloadService"/> class.</summary>
    /// <param name="store">Resolves the report and the owner of the record it belongs to.</param>
    /// <param name="grantChecker">The database-backed grant check a doctor's download must pass.</param>
    /// <param name="storage">The private file store.</param>
    /// <param name="auditWriter">Writes the download audit row.</param>
    public ReportDownloadService(
        IReportStore store,
        IAccessGrantChecker grantChecker,
        IReportStorage storage,
        IAuditWriter auditWriter)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(grantChecker);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(auditWriter);

        _store = store;
        _grantChecker = grantChecker;
        _storage = storage;
        _auditWriter = auditWriter;
    }

    /// <inheritdoc />
    public async Task<ReportDownloadResult> DownloadAsync(
        Guid reportId,
        Guid callerUserId,
        ReportUploaderRole callerRole,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

        if (reportId == Guid.Empty || callerUserId == Guid.Empty)
        {
            return ReportDownloadResult.Denied(ReportDownloadFailure.NotFound);
        }

        var scope = await _store.FindReportForDownloadAsync(reportId, cancellationToken);

        if (scope is null)
        {
            return ReportDownloadResult.Denied(ReportDownloadFailure.NotFound);
        }

        var accessGrantId = await AuthorizeAsync(scope, callerUserId, callerRole, cancellationToken);

        if (accessGrantId.IsRefused)
        {
            return ReportDownloadResult.Denied(ReportDownloadFailure.NotFound);
        }

        Stream? stream = null;

        try
        {
            stream = await _storage.OpenReadAsync(scope.StoredFileName, cancellationToken);

            if (stream is null)
            {
                // The row exists but the file does not: treat as unavailable without revealing which is the case.
                return ReportDownloadResult.Denied(ReportDownloadFailure.NotFound);
            }

            // The audit row is written before the content leaves the service, so a downloaded file always has its row
            // (FR-PRV-02, ADR-0004).
            await _auditWriter.WriteAsync(
                new AuditEntry(
                    callerUserId,
                    scope.PatientUserId,
                    AuditAction.ReportDownloaded,
                    AuditEntityType.Report,
                    scope.ReportId.ToString(),
                    ipAddress,
                    accessGrantId.GrantId),
                cancellationToken);

            return ReportDownloadResult.Success(stream, scope.ContentType);
        }
        catch
        {
            if (stream is not null)
            {
                await stream.DisposeAsync();
            }

            throw;
        }
    }

    private async Task<AuthorizationOutcome> AuthorizeAsync(
        ReportDownloadScope scope,
        Guid callerUserId,
        ReportUploaderRole callerRole,
        CancellationToken cancellationToken)
    {
        switch (callerRole)
        {
            case ReportUploaderRole.Patient:
                return scope.PatientUserId == callerUserId
                    ? new AuthorizationOutcome(false, null)
                    : new AuthorizationOutcome(true, null);

            case ReportUploaderRole.Doctor:
                var check = await _grantChecker.RequireActiveGrantForPatientAsync(
                    callerUserId,
                    scope.PatientUserId,
                    cancellationToken);

                if (!check.IsAllowed)
                {
                    return new AuthorizationOutcome(true, null);
                }

                var grant = check.Grant
                    ?? throw new InvalidOperationException("An allowed grant result must carry a grant.");

                // Defence in depth: a grant for another patient authorizes nothing.
                if (grant.PatientId != scope.PatientUserId)
                {
                    return new AuthorizationOutcome(true, null);
                }

                return new AuthorizationOutcome(false, grant.Id);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(callerRole),
                    callerRole,
                    "Unknown report caller role.");
        }
    }

    private readonly record struct AuthorizationOutcome(bool IsRefused, Guid? GrantId);
}

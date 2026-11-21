namespace MediPassPlus.Application.Records;

/// <summary>
/// The report upload use case (FR-REC-05, FR-REC-06, UC-16). One implementation serves both callers: a patient on their
/// own record and a doctor holding an active grant, and the difference is decided from the authenticated identity
/// plus a database check on every request (FR-PRV-01, ADR-0015).
/// </summary>
public interface IReportUploadService
{
    /// <summary>
    /// Authorizes the caller against the record, validates the file, stores it privately and commits the report row with
    /// its audit entry.
    /// </summary>
    /// <param name="recordId">The record identifier from the route.</param>
    /// <param name="callerUserId">The authenticated caller's user account identifier.</param>
    /// <param name="callerRole">Whether the caller is the patient or a doctor, taken from the token's role.</param>
    /// <param name="file">The uploaded part.</param>
    /// <param name="ipAddress">The connection IP address recorded in the audit row (ADR-0004).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored report's metadata, or the refusal reason.</returns>
    Task<ReportUploadResult> UploadAsync(
        Guid recordId,
        Guid callerUserId,
        ReportUploaderRole callerRole,
        IUploadedReportFile file,
        string ipAddress,
        CancellationToken cancellationToken = default);
}

namespace MediPassPlus.Application.Records;

/// <summary>
/// The report download/open use case (FR-REC-08, FR-PRV-05). A patient may read their own report and a doctor may read
/// a report only while holding an active grant for the patient who owns the record. Every successful access writes a
/// <c>ReportDownloaded</c> audit row (FR-PRV-02, ADR-0004).
/// </summary>
public interface IReportDownloadService
{
    /// <summary>
    /// Authorizes the caller against the stored report, opens the file, writes the audit row, and returns the stream.
    /// </summary>
    /// <param name="reportId">The report identifier from the route.</param>
    /// <param name="callerUserId">The authenticated caller's user account identifier.</param>
    /// <param name="callerRole">Whether the caller is the patient or a doctor, taken from the token's role.</param>
    /// <param name="ipAddress">The connection IP address recorded in the audit row (ADR-0004).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The file stream and content type, or a refusal reason.</returns>
    Task<ReportDownloadResult> DownloadAsync(
        Guid reportId,
        Guid callerUserId,
        ReportUploaderRole callerRole,
        string ipAddress,
        CancellationToken cancellationToken = default);
}

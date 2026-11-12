namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Ends a live access grant immediately at the patient's request (FR-ACS-09, UC-19).
/// </summary>
public interface IAccessGrantEndService
{
    /// <summary>
    /// Ends the grant when the authenticated patient owns its appointment and the grant is still active. The end and
    /// its <c>AccessEnded</c> audit row commit in one unit of work (ADR-0004 decision 7).
    /// </summary>
    /// <param name="grantId">The grant to end.</param>
    /// <param name="patientUserId">The authenticated patient user account identifier.</param>
    /// <param name="ipAddress">The connection IP address recorded on the audit row.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ended grant or a refusal reason.</returns>
    Task<AccessGrantEndResult> EndAsync(
        Guid grantId,
        Guid patientUserId,
        string ipAddress,
        CancellationToken cancellationToken = default);
}

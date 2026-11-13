namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Answers a pending access request on behalf of its patient. Approval creates the visit's access grant; rejection
/// creates none. The caller must pass the authenticated patient's user account identifier, never an identifier
/// supplied by the client (FR-ACS-03, FR-ACS-04, FR-ACS-06).
/// </summary>
public interface IAccessRequestDecisionService
{
    /// <summary>Approves a pending request and creates its access grant.</summary>
    /// <param name="requestId">The request being approved.</param>
    /// <param name="patientUserId">The authenticated patient's user account identifier.</param>
    /// <param name="ipAddress">The connection IP address recorded on the audit row (ADR-0004 decision 8).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created grant or a refusal reason.</returns>
    Task<AccessRequestDecisionResult> ApproveAsync(
        Guid requestId,
        Guid patientUserId,
        string ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Rejects a pending request without creating an access grant.</summary>
    /// <param name="requestId">The request being rejected.</param>
    /// <param name="patientUserId">The authenticated patient's user account identifier.</param>
    /// <param name="ipAddress">The connection IP address recorded on the audit row (ADR-0004 decision 8).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A successful result without a grant, or a refusal reason.</returns>
    Task<AccessRequestDecisionResult> RejectAsync(
        Guid requestId,
        Guid patientUserId,
        string ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves the pending request proved by a scanned QR token and creates its access grant. The token is
    /// resolved by its stored hash; an unknown or foreign token is refused without revealing which (UC-12,
    /// FR-ACS-03, FR-ACS-04, FR-ACS-06).
    /// </summary>
    /// <param name="token">The plaintext QR token presented by the patient.</param>
    /// <param name="patientUserId">The authenticated patient's user account identifier.</param>
    /// <param name="ipAddress">The connection IP address recorded on the audit row (ADR-0004 decision 8).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created grant or a refusal reason.</returns>
    Task<AccessRequestDecisionResult> ApproveByTokenAsync(
        string token,
        Guid patientUserId,
        string ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects the pending request proved by a scanned QR token without creating an access grant.
    /// </summary>
    /// <param name="token">The plaintext QR token presented by the patient.</param>
    /// <param name="patientUserId">The authenticated patient's user account identifier.</param>
    /// <param name="ipAddress">The connection IP address recorded on the audit row (ADR-0004 decision 8).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A successful result without a grant, or a refusal reason.</returns>
    Task<AccessRequestDecisionResult> RejectByTokenAsync(
        string token,
        Guid patientUserId,
        string ipAddress,
        CancellationToken cancellationToken = default);
}

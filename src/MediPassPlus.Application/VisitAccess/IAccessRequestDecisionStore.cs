using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The unit of work for answering an access request. The read side resolves the request and its tracked scope; the
/// write side stages the grant and commits the request, credential and audit changes together.
/// </summary>
public interface IAccessRequestDecisionStore
{
    /// <summary>Finds a request by its identifier without tracking it.</summary>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request, or null when it does not exist.</returns>
    Task<AccessRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the tracked request, appointment and credentials for a verified scope, or null when they do not match.
    /// </summary>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="scope">The appointment scope verified for the authenticated patient.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked decision state, or null when the request no longer matches the scope.</returns>
    Task<AccessRequestDecisionContext?> LoadAsync(
        Guid requestId,
        VisitAccessScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the tracked credential, request, appointment and every credential for a presented token hash. The
    /// patient is checked against the stored request and appointment by the caller.
    /// </summary>
    /// <param name="tokenHash">The SHA-256 hash of the presented QR token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked token decision state, or null when no QR credential has that hash.</returns>
    Task<AccessRequestTokenContext?> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>Stages the grant created by an approval.</summary>
    /// <param name="grant">The grant to add.</param>
    void AddGrant(AccessGrant grant);

    /// <summary>Commits the staged request, grant, credential and audit changes.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="AccessRequestDecisionConflictException">
    /// Another decision won the race or the one-grant-per-appointment guard rejected the write.
    /// </exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

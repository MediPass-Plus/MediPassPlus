using MediPassPlus.Application.Dtos.Auth;
using MediPassPlus.Domain.Identity;

namespace MediPassPlus.Application.Auth;

/// <summary>
/// Issues, rotates and revokes refresh tokens (FR-ACC-04, FR-ACC-06, NFR-SEC-03).
/// </summary>
public interface IAuthTokenService
{
    /// <summary>
    /// Starts a session: mints an access token and stores the first refresh token of the session.
    /// </summary>
    /// <param name="user">The authenticated account. It must already be active.</param>
    /// <param name="deviceId">Optional client label, stored for support only.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The access token, the refresh token in plaintext and the token lifetimes.</returns>
    Task<AuthTokensResponse> IssueAsync(
        ApplicationUser user,
        string? deviceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges a refresh token for a new pair. The presented row is revoked and linked to its replacement, so
    /// the same value can never be exchanged twice; presenting a token that was already replaced revokes the
    /// account's other live tokens as well, because it is the signature of a stolen token.
    /// </summary>
    /// <param name="refreshToken">The plaintext token presented by the client.</param>
    /// <param name="deviceId">Optional client label for the replacement.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>
    /// The new pair, or <c>null</c> when the token is unknown, revoked, expired, owned by a deactivated account or
    /// past the session cap. The caller answers every one of those with the same <c>401</c>.
    /// </returns>
    Task<AuthTokensResponse?> RotateAsync(
        string refreshToken,
        string? deviceId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ends the session that owns the presented token. Unknown and already-revoked tokens are ignored, so the
    /// endpoint is idempotent and cannot be used to discover which tokens exist.
    /// </summary>
    /// <param name="refreshToken">The plaintext token presented by the client.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>A task that completes when the row is revoked or nothing had to change.</returns>
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);
}

using MediPassPlus.Application.Auth;
using MediPassPlus.Application.Dtos.Auth;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Extensions;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace MediPassPlus.Infrastructure.Auth;

/// <summary>
/// The refresh-token lifecycle (FR-ACC-04, FR-ACC-06, NFR-SEC-03).
///
/// A token is 256 bits of CSPRNG output and only its SHA-256 hash is stored. Every rotation revokes the presented
/// row and links it to its replacement, so the same value can never be exchanged twice. Presenting a revoked value
/// revokes the account's other live tokens too, because it is the only signal the server gets that a token leaked.
/// </summary>
public sealed class AuthTokenService : IAuthTokenService
{
    /// <summary>32 random bytes; the token is opaque, so the length is the whole of its strength.</summary>
    private const int TokenBytes = 32;

    private readonly MediPassDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly JwtTokenService _jwt;
    private readonly JwtSettings _settings;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the service.</summary>
    /// <param name="context">The database context that owns the token rows.</param>
    /// <param name="userManager">Resolves the account and its current roles.</param>
    /// <param name="jwt">Mints the access token.</param>
    /// <param name="settings">The configured token lifetimes.</param>
    /// <param name="timeProvider">The clock, so tests can move time instead of waiting.</param>
    public AuthTokenService(
        MediPassDbContext context,
        UserManager<ApplicationUser> userManager,
        JwtTokenService jwt,
        IOptions<JwtSettings> settings,
        TimeProvider timeProvider)
    {
        _context = context;
        _userManager = userManager;
        _jwt = jwt;
        _settings = settings.Value;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<AuthTokensResponse> IssueAsync(
        ApplicationUser user,
        string? deviceId,
        CancellationToken cancellationToken)
    {
        var now = Now();
        var token = NewToken();

        var row = RefreshToken.Issue(
            user.Id,
            RefreshTokenHasher.Hash(token),
            now,
            TokenLifetime,
            SessionLifetime,
            deviceId);

        _context.RefreshTokens.Add(row);
        await _context.SaveChangesAsync(cancellationToken);

        return await BuildResponseAsync(user, token, row.ExpiresAtUtc);
    }

    /// <inheritdoc />
    public async Task<AuthTokensResponse?> RotateAsync(
        string refreshToken,
        string? deviceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var now = Now();
        var hash = RefreshTokenHasher.Hash(refreshToken);

        var current = await _context.RefreshTokens
            .SingleOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);

        if (current is null)
        {
            return null;
        }

        var user = await _userManager.FindByIdAsync(current.UserId.ToString());

        // A deactivated account (FR-STF-07) never gets a new pair, even with a token that is still valid.
        if (user is null || !user.IsActive)
        {
            return null;
        }

        if (current.IsRevoked)
        {
            // A retired token that has a successor is a copy that leaked: the legitimate client already moved on to
            // the replacement, so anyone still presenting this value holds a stolen copy. Revoking every live token
            // of the account contains the theft, and the client has to sign in again to open a new session
            // (FR-ACC-06). A token revoked by a logout has no successor and ends nothing but itself: a client that
            // presents it again is refused without touching the account's other sessions.
            if (current.ReplacedByTokenId is not null)
            {
                await RevokeAllActiveAsync(user.Id, now, cancellationToken);
            }

            return null;
        }

        if (!current.IsActive(now))
        {
            return null;
        }

        var sessionCap = current.SessionStartedAtUtc + SessionLifetime;

        // The token is still inside its own lifetime but the session reached its absolute limit: only a fresh login
        // may continue.
        if (sessionCap <= now)
        {
            return null;
        }

        var replacementToken = NewToken();

        var replacement = RefreshToken.Issue(
            user.Id,
            RefreshTokenHasher.Hash(replacementToken),
            now,
            TokenLifetime,
            SessionLifetime,
            deviceId ?? current.DeviceId,
            current.SessionStartedAtUtc);

        current.Revoke(now, replacement.Id);
        _context.RefreshTokens.Add(replacement);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request rotated or revoked the same row first; that request owns the session now.
            return null;
        }

        return await BuildResponseAsync(user, replacementToken, replacement.ExpiresAtUtc);
    }

    /// <inheritdoc />
    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var hash = RefreshTokenHasher.Hash(refreshToken);

        // A direct UPDATE keeps the endpoint idempotent and race-free without loading the row: an unknown token, an
        // already-revoked token and a successful logout all end the same way (NFR-SEC-10).
        await _context.RefreshTokens
            .Where(token => token.TokenHash == hash && token.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAtUtc, (DateTime?)Now()),
                cancellationToken);
    }

    private TimeSpan TokenLifetime => TimeSpan.FromDays(_settings.RefreshTokenDays);

    private TimeSpan SessionLifetime => TimeSpan.FromDays(_settings.SessionDays);

    private DateTime Now() => _timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>Creates an opaque token; URL-safe so it survives a query string or a header unchanged.</summary>
    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private async Task<AuthTokensResponse> BuildResponseAsync(
        ApplicationUser user,
        string refreshToken,
        DateTime refreshTokenExpiresAtUtc)
    {
        // Read the roles on every issue, so a role change takes effect at the next refresh instead of waiting for
        // the session to end.
        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _jwt.CreateAccessToken(user, roles);

        return new AuthTokensResponse(
            accessToken,
            refreshToken,
            refreshTokenExpiresAtUtc,
            _settings.AccessTokenMinutes * 60,
            roles.ToArray());
    }

    private async Task RevokeAllActiveAsync(
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // One statement, so a concurrent rotation cannot make the batch fail halfway through. The presented token is
        // refused whatever the outcome, and the account has to sign in again to open a new session.
        await _context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAtUtc, (DateTime?)now),
                cancellationToken);
    }
}

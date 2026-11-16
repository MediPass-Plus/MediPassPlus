namespace MediPassPlus.Domain.Identity;

/// <summary>
/// One issued refresh token (FR-ACC-04, FR-ACC-06, NFR-SEC-03).
///
/// Only the SHA-256 hash of the token is stored, so a leaked row cannot be replayed and the plaintext never
/// reaches the database or a log. A token is single use: a refresh revokes it and links it to the token that
/// replaced it, which is what makes a second presentation of the same value detectable as a replay.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    /// <summary>Identifier of the stored row; also the value a rotation writes into <see cref="ReplacedByTokenId"/>.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account the token belongs to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Upper-case hex SHA-256 of the presented token. The token itself is never stored.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>When the token was issued, in UTC.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>When the token stops being accepted, in UTC. Never later than the session cap.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>
    /// When the session that owns this token started, in UTC. Every rotation copies it, so the absolute session
    /// cap survives an unlimited number of rotations.
    /// </summary>
    public DateTime SessionStartedAtUtc { get; private set; }

    /// <summary>
    /// When the token was revoked, by a rotation, a logout or a detected replay, in UTC. This is the concurrency
    /// token, so two requests rotating the same row cannot both win.
    /// </summary>
    public DateTime? RevokedAtUtc { get; private set; }

    /// <summary>The token that replaced this one, when a rotation revoked it.</summary>
    public Guid? ReplacedByTokenId { get; private set; }

    /// <summary>Optional client-supplied label, kept for support only; it never grants anything.</summary>
    public string? DeviceId { get; private set; }

    /// <summary>Whether the row has been revoked.</summary>
    public bool IsRevoked => RevokedAtUtc is not null;

    /// <summary>
    /// Issues a token that expires after <paramref name="lifetime"/> but never after the session cap, so a
    /// rotation cannot extend a session past its absolute limit.
    /// </summary>
    /// <param name="userId">The account the token belongs to.</param>
    /// <param name="tokenHash">The SHA-256 hash of the token that will be handed to the client.</param>
    /// <param name="nowUtc">The current UTC time.</param>
    /// <param name="lifetime">How long a single token stays valid.</param>
    /// <param name="sessionLifetime">The absolute limit of the whole session, counted from its first login.</param>
    /// <param name="deviceId">Optional client label.</param>
    /// <param name="sessionStartedAtUtc">
    /// The start of the session; pass the previous token's value on a rotation and omit it on a login.
    /// </param>
    /// <returns>The new row.</returns>
    /// <exception cref="ArgumentException">The hash is empty or the session start is in the future.</exception>
    /// <exception cref="InvalidOperationException">The session cap has already elapsed.</exception>
    public static RefreshToken Issue(
        Guid userId,
        string tokenHash,
        DateTime nowUtc,
        TimeSpan lifetime,
        TimeSpan sessionLifetime,
        string? deviceId = null,
        DateTime? sessionStartedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("A refresh token hash is required.", nameof(tokenHash));
        }

        var sessionStart = sessionStartedAtUtc ?? nowUtc;

        if (sessionStart > nowUtc)
        {
            throw new ArgumentException("A session cannot start in the future.", nameof(sessionStartedAtUtc));
        }

        var sessionCap = sessionStart + sessionLifetime;
        var expiresAt = nowUtc + lifetime;

        if (expiresAt > sessionCap)
        {
            expiresAt = sessionCap;
        }

        if (expiresAt <= nowUtc)
        {
            throw new InvalidOperationException("The session lifetime has already elapsed.");
        }

        return new RefreshToken
        {
            // v7 keeps the insert order of the clustered primary key, like every other entity in the model.
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAt,
            SessionStartedAtUtc = sessionStart,
            DeviceId = deviceId
        };
    }

    /// <summary>Whether the token can still be exchanged, at <paramref name="nowUtc"/>.</summary>
    /// <param name="nowUtc">The current UTC time.</param>
    /// <returns><c>true</c> when the token is neither revoked nor expired.</returns>
    public bool IsActive(DateTime nowUtc) => !IsRevoked && ExpiresAtUtc > nowUtc;

    /// <summary>
    /// Revokes the token once. A second call is a no-op, so a logout racing a rotation stays idempotent.
    /// </summary>
    /// <param name="nowUtc">The current UTC time.</param>
    /// <param name="replacedByTokenId">The row that replaced this one, when a rotation revoked it.</param>
    public void Revoke(DateTime nowUtc, Guid? replacedByTokenId = null)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAtUtc = nowUtc;
        ReplacedByTokenId = replacedByTokenId;
    }
}

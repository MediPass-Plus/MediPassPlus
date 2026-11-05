namespace MediPassPlus.Domain.VisitAccess;

/// <summary>
/// A single-use secret that proves an <see cref="AccessRequest"/>: the QR token or the six-digit fallback.
/// Only the hash is stored; the plaintext is never persisted (ADR-0001).
/// </summary>
public sealed class VisitAccessCredential
{
    /// <summary>Wrong code entries allowed before the credential is locked (FR-ACS-05).</summary>
    public const int MaxFailedAttempts = 5;

    private VisitAccessCredential()
    {
    }

    private VisitAccessCredential(
        Guid id,
        Guid accessRequestId,
        VisitCredentialType type,
        string secretHash,
        DateTime createdAtUtc,
        DateTime expiresAtUtc)
    {
        Id = id;
        AccessRequestId = accessRequestId;
        Type = type;
        SecretHash = secretHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; }

    /// <summary>The request this credential proves.</summary>
    public Guid AccessRequestId { get; private set; }

    /// <summary>Whether this is the QR token or the numeric code.</summary>
    public VisitCredentialType Type { get; private set; }

    /// <summary>The SHA-256 (QR) or HMAC-SHA-256 (code) hash of the secret.</summary>
    public string SecretHash { get; private set; } = string.Empty;

    /// <summary>When the credential was generated, in UTC.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>When the credential stops being valid, in UTC (two minutes for QR, five for the code).</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>When the credential was used successfully, in UTC.</summary>
    public DateTime? ConsumedAtUtc { get; private set; }

    /// <summary>When the credential was revoked, for example when a newer code replaced it (US-22).</summary>
    public DateTime? RevokedAtUtc { get; private set; }

    /// <summary>Number of wrong code entries recorded so far.</summary>
    public int FailedAttempts { get; private set; }

    /// <summary>When the credential was locked after too many wrong code entries (FR-ACS-05).</summary>
    public DateTime? LockedAtUtc { get; private set; }

    /// <summary>Creates a credential that expires at the supplied instant.</summary>
    /// <param name="accessRequestId">The request this credential proves.</param>
    /// <param name="type">The credential type, which fixes its hash algorithm and lifetime policy.</param>
    /// <param name="secretHash">The hash of the secret; the plaintext must never be passed here.</param>
    /// <param name="expiresAtUtc">The expiry instant, in UTC.</param>
    /// <param name="utcNow">The current UTC time.</param>
    public static VisitAccessCredential Create(
        Guid accessRequestId,
        VisitCredentialType type,
        string secretHash,
        DateTime expiresAtUtc,
        DateTime utcNow)
    {
        if (accessRequestId == Guid.Empty)
        {
            throw new ArgumentException("The access request id is required.", nameof(accessRequestId));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        if (string.IsNullOrWhiteSpace(secretHash))
        {
            throw new ArgumentException("The secret hash is required.", nameof(secretHash));
        }

        if (expiresAtUtc <= utcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "The credential must expire in the future.");
        }

        return new VisitAccessCredential(
            Guid.CreateVersion7(),
            accessRequestId,
            type,
            secretHash,
            utcNow,
            expiresAtUtc);
    }

    /// <summary>True when the credential is unused, not revoked or locked, and not expired.</summary>
    /// <param name="utcNow">The current UTC time.</param>
    public bool IsUsableAt(DateTime utcNow) =>
        ConsumedAtUtc is null
        && RevokedAtUtc is null
        && LockedAtUtc is null
        && utcNow < ExpiresAtUtc;

    /// <summary>Marks the credential as used by a successful approval.</summary>
    /// <param name="utcNow">The current UTC time.</param>
    public void Consume(DateTime utcNow)
    {
        EnsureUsable(utcNow);
        ConsumedAtUtc = utcNow;
    }

    /// <summary>Revokes the credential. Revoking an already revoked credential has no effect.</summary>
    /// <param name="utcNow">The current UTC time.</param>
    public void Revoke(DateTime utcNow)
    {
        RevokedAtUtc ??= utcNow;
    }

    /// <summary>Records a wrong six-digit code entry and locks the credential at the limit (FR-ACS-05).</summary>
    /// <param name="utcNow">The current UTC time.</param>
    public void RegisterFailedAttempt(DateTime utcNow)
    {
        if (Type != VisitCredentialType.NumericCode)
        {
            throw new InvalidOperationException("Failed attempts apply only to the numeric code.");
        }

        EnsureUsable(utcNow);

        FailedAttempts++;

        if (FailedAttempts >= MaxFailedAttempts)
        {
            LockedAtUtc = utcNow;
        }
    }

    private void EnsureUsable(DateTime utcNow)
    {
        if (!IsUsableAt(utcNow))
        {
            throw new InvalidOperationException("The credential is not usable.");
        }
    }
}

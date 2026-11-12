using MediPassPlus.Application.VisitAccess;

namespace MediPassPlus.Infrastructure.VisitAccess;

/// <summary>
/// Configuration for the six-digit fallback. <see cref="HmacKey"/> is the Base64 encoding of at least 32 random
/// bytes and is a third secret, separate from the JWT signing key and the Data Protection ring (ADR-0007 decision 5).
/// It is supplied through user-secrets or an environment variable and never committed.
/// </summary>
public sealed class AccessCodeOptions
{
    /// <summary>The configuration section that binds these options.</summary>
    public const string SectionName = "AccessCode";

    /// <summary>Gets or sets the Base64-encoded HMAC key.</summary>
    public string HmacKey { get; set; } = string.Empty;

    /// <summary>
    /// Decodes the configured key, or returns false when it is missing, not Base64, or shorter than
    /// <see cref="HmacAccessCodeHasher.MinimumKeySizeBytes"/>.
    /// </summary>
    /// <param name="key">The decoded key when the method returns true; otherwise an empty array.</param>
    /// <returns>True when the configured key is usable.</returns>
    public bool TryGetKeyBytes(out byte[] key)
    {
        key = [];

        if (string.IsNullOrWhiteSpace(HmacKey))
        {
            return false;
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(HmacKey);
        }
        catch (FormatException)
        {
            return false;
        }

        if (decoded.Length < HmacAccessCodeHasher.MinimumKeySizeBytes)
        {
            return false;
        }

        key = decoded;
        return true;
    }

    /// <summary>Returns the decoded HMAC key.</summary>
    /// <returns>The key bytes.</returns>
    /// <exception cref="InvalidOperationException">The configured key is missing, not Base64, or too short.</exception>
    public byte[] GetKeyBytes()
    {
        if (!TryGetKeyBytes(out var key))
        {
            throw new InvalidOperationException(
                $"AccessCode:HmacKey must be the Base64 encoding of at least "
                + $"{HmacAccessCodeHasher.MinimumKeySizeBytes} random bytes.");
        }

        return key;
    }
}

using System.Security.Cryptography;
using System.Text;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// HMAC-SHA-256 hasher for the six-digit fallback code. The key is a protected server-side secret that is separate
/// from the JWT signing key and the Data Protection ring (ADR-0007 decision 5). Verification uses a fixed-time
/// comparison and never throws on a malformed stored value.
/// </summary>
public sealed class HmacAccessCodeHasher : IAccessCodeHasher
{
    /// <summary>The minimum key size in bytes. A shorter key weakens the HMAC below the 256-bit hash size.</summary>
    public const int MinimumKeySizeBytes = 32;

    private readonly byte[] _key;

    /// <summary>Initializes a new instance of the <see cref="HmacAccessCodeHasher"/> class.</summary>
    /// <param name="key">The raw HMAC key; at least <see cref="MinimumKeySizeBytes"/> bytes.</param>
    /// <exception cref="ArgumentException">The key is shorter than the minimum.</exception>
    public HmacAccessCodeHasher(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length < MinimumKeySizeBytes)
        {
            throw new ArgumentException(
                $"The access-code HMAC key must be at least {MinimumKeySizeBytes} bytes.",
                nameof(key));
        }

        _key = key.ToArray();
    }

    /// <inheritdoc />
    public string Hash(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(code));
        return Convert.ToBase64String(hash);
    }

    /// <inheritdoc />
    public bool Verify(string code, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        byte[] storedBytes;
        try
        {
            storedBytes = Convert.FromBase64String(storedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var computed = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(code));

        return CryptographicOperations.FixedTimeEquals(computed, storedBytes);
    }
}

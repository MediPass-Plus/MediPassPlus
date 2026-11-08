using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Generates the one-time QR token for a visit-access request and hashes it for storage (FR-ACS-02, ADR-0001).
/// </summary>
public static class QrTokenGenerator
{
    /// <summary>The token size in bytes: 128 bits of CSPRNG entropy.</summary>
    public const int TokenSizeBytes = 16;

    /// <summary>
    /// Creates a new 128-bit CSPRNG token, Base64Url-encodes it, and returns the plaintext together with its
    /// SHA-256 hash. The plaintext is returned to the doctor once and must never be persisted or logged.
    /// </summary>
    /// <returns>The generated token and the hash that is stored for it.</returns>
    public static QrToken Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenSizeBytes);
        var value = Base64Url.EncodeToString(bytes);

        return new QrToken(value, Hash(value));
    }

    /// <summary>
    /// Returns the uppercase hexadecimal SHA-256 hash of the UTF-8 bytes of <paramref name="token"/>.
    /// The approval endpoint must hash the presented token with this same method.
    /// </summary>
    /// <param name="token">The plaintext Base64Url QR token.</param>
    /// <returns>The 64-character uppercase hexadecimal hash.</returns>
    /// <exception cref="ArgumentException">The token is null, empty or whitespace.</exception>
    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}

/// <summary>A newly generated QR token and the hash that is stored for it.</summary>
/// <param name="Value">The plaintext Base64Url token. It is returned once and never persisted.</param>
/// <param name="Sha256Hash">The uppercase hexadecimal SHA-256 hash stored in the database.</param>
public sealed record QrToken(string Value, string Sha256Hash);

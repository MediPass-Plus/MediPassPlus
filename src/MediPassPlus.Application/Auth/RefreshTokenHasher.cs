using System.Security.Cryptography;
using System.Text;

namespace MediPassPlus.Application.Auth;

/// <summary>
/// The one place a refresh token is turned into the value that is stored (FR-ACC-06, NFR-SEC-03).
///
/// A plain SHA-256 is enough here, unlike the six-digit visit code that needs an HMAC key (ADR-0002): the token
/// is 256 bits of CSPRNG output, so the keyspace cannot be enumerated and there is nothing to precompute.
/// </summary>
public static class RefreshTokenHasher
{
    /// <summary>Hashes a presented refresh token for storage and lookup.</summary>
    /// <param name="token">The plaintext token. It is never stored or logged.</param>
    /// <returns>The upper-case hex SHA-256 of the token.</returns>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

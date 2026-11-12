using System.Globalization;
using System.Security.Cryptography;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Generates the six-digit fallback code with the platform CSPRNG (FR-ACS-05, ADR-0001). The value is uniformly
/// distributed over <c>000000</c>..<c>999999</c>, so leading zeros are valid and every code has the same chance.
/// </summary>
public static class AccessCodeGenerator
{
    /// <summary>The number of digits in a code.</summary>
    public const int Length = 6;

    private const int ExclusiveUpperBound = 1_000_000;

    /// <summary>Creates a new six-digit code. The plaintext is returned once and must never be persisted or logged.</summary>
    /// <returns>The six-character code, including any leading zeros.</returns>
    public static string Generate() =>
        RandomNumberGenerator.GetInt32(ExclusiveUpperBound).ToString("D6", CultureInfo.InvariantCulture);
}

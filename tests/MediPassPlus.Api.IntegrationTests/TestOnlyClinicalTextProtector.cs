using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MediPassPlus.Application.Privacy;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// A stand-in <see cref="IClinicalTextProtector"/> used only by this HTTP harness. The real implementation, the Data
/// Protection key ring and both versioned purposes belong to MP-50 (NFR-SEC-05, ADR-0007 decision 5); until that
/// ticket lands there is no registration in the API at all, so MVC fails while it activates every record-write
/// controller and the harness can observe no refusal at all.
/// </summary>
/// <remarks>
/// The double is reversible and marks every value it produces, so no test can mistake it for encryption. It exists so
/// the negative tests can reach the authorization and access-grant filters; a happy-path test must wait for MP-50 and
/// then assert through the real implementation. Nothing in <c>src</c> references this type.
/// </remarks>
internal sealed class TestOnlyClinicalTextProtector : IClinicalTextProtector
{
    private const string Marker = "test-only-stand-in:";

    /// <inheritdoc />
    public string Protect(ClinicalTextPurpose purpose, string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "The clinical text purpose is undefined.");
        }

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Marker}{purpose}:{encoded}");
    }

    /// <inheritdoc />
    public string Unprotect(ClinicalTextPurpose purpose, string ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        var prefix = string.Create(CultureInfo.InvariantCulture, $"{Marker}{purpose}:");
        if (!ciphertext.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("The payload was not produced by this stand-in.");
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext[prefix.Length..]));
    }
}

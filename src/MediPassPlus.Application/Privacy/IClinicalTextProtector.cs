namespace MediPassPlus.Application.Privacy;

/// <summary>
/// The only component allowed to encrypt and decrypt clinical text (NFR-SEC-05, ADR-0007 decision 5). Callers pass
/// plaintext only across this boundary; the implementation maps each <see cref="ClinicalTextPurpose"/> to its
/// versioned Data Protection purpose and never falls back to plaintext on failure.
/// </summary>
/// <remarks>
/// The interface lives in Application so the Record write path depends on a contract, not on Data Protection. The
/// key ring, its outside-the-repository path and the startup check are configured by the implementation (MP-50).
/// </remarks>
public interface IClinicalTextProtector
{
    /// <summary>Encrypts a plaintext clinical value under the requested purpose.</summary>
    /// <param name="purpose">The field family the value belongs to.</param>
    /// <param name="plaintext">The value to encrypt; never stored as-is.</param>
    /// <returns>The Base64 ciphertext to persist.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="plaintext"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="purpose"/> is not defined.</exception>
    string Protect(ClinicalTextPurpose purpose, string plaintext);

    /// <summary>Decrypts a stored ciphertext under the requested purpose.</summary>
    /// <param name="purpose">The field family the value belongs to.</param>
    /// <param name="ciphertext">The stored ciphertext.</param>
    /// <returns>The original plaintext.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ciphertext"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="purpose"/> is not defined.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The payload was not produced under this purpose, or the key is unavailable.
    /// </exception>
    string Unprotect(ClinicalTextPurpose purpose, string ciphertext);
}

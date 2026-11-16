namespace MediPassPlus.Application.Privacy;

/// <summary>
/// The only service allowed to encrypt and decrypt diagnosis text (FR-PRV-04, NFR-SEC-05, ADR-0007 decision 5).
/// Implementations derive a key from the Data Protection ring under the versioned purpose
/// <c>MediPass.Diagnosis.v1</c>, so the stored value is never the plaintext and the scheme can be versioned later.
/// Plaintext and ciphertext are never logged.
/// </summary>
public interface IDiagnosisProtector
{
    /// <summary>Encrypts one diagnosis text under the versioned diagnosis purpose.</summary>
    /// <param name="plaintext">The diagnosis text to encrypt. It is never stored as supplied.</param>
    /// <returns>The Base64Url Data Protection payload that is stored in <c>records.Diagnoses.Description</c>.</returns>
    /// <exception cref="ArgumentException">The plaintext is null, empty or whitespace.</exception>
    string Protect(string plaintext);

    /// <summary>Decrypts a stored diagnosis text.</summary>
    /// <param name="ciphertext">The Base64Url payload previously produced by <see cref="Protect"/>.</param>
    /// <returns>The original plaintext.</returns>
    /// <exception cref="ArgumentException">The ciphertext is null, empty or whitespace.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The payload was not produced by this ring and purpose, or it has been tampered with.
    /// </exception>
    string Unprotect(string ciphertext);
}

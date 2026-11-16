using MediPassPlus.Application.Privacy;
using Microsoft.AspNetCore.DataProtection;

namespace MediPassPlus.Infrastructure.Privacy;

/// <summary>
/// Encrypts and decrypts diagnosis text with the application's Data Protection key ring (FR-PRV-04, NFR-SEC-05).
/// The purpose string is versioned: a future scheme gets a new purpose, and a payload produced under
/// <see cref="Purpose"/> can only be read under that same purpose (ADR-0005, ADR-0007 decision 5).
/// </summary>
public sealed class DiagnosisProtector : IDiagnosisProtector
{
    /// <summary>
    /// The versioned Data Protection purpose for diagnosis text. It is persisted implicitly in every stored
    /// payload, so changing it is a data migration, not a configuration change (ADR-0005).
    /// </summary>
    public const string Purpose = "MediPass.Diagnosis.v1";

    private readonly IDataProtector _protector;

    /// <summary>Creates the protector from the configured ring under <see cref="Purpose"/>.</summary>
    /// <param name="dataProtectionProvider">The application's Data Protection provider.</param>
    /// <exception cref="ArgumentNullException">The provider is null.</exception>
    public DiagnosisProtector(IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        _protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return _protector.Protect(plaintext);
    }

    /// <inheritdoc />
    public string Unprotect(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);
        return _protector.Unprotect(ciphertext);
    }
}

using MediPassPlus.Application.Privacy;
using Microsoft.AspNetCore.DataProtection;

namespace MediPassPlus.Infrastructure.Privacy;

/// <summary>
/// The purpose-aware clinical text encryptor the Record write path consumes (NFR-SEC-05, FR-PRV-04, ADR-0007
/// decision 5). It adds no second mechanism and no second key ring: the diagnosis purpose delegates to the
/// <see cref="IDiagnosisProtector"/> registered by MP-50, so the stored payload and its versioned purpose are exactly
/// the ones that ticket defines, while the prescription purpose is derived from the same ring under
/// <see cref="ClinicalTextPurposeNames.Prescription"/> (ADR-0005).
/// </summary>
/// <remarks>
/// A value encrypted under one purpose is refused under the other, because Data Protection folds the purpose string
/// into the payload. That isolation is what keeps a prescription field from being readable as diagnosis text. The
/// protector never falls back to plaintext: an undefined purpose and any cryptographic failure surface as exceptions.
/// </remarks>
public sealed class ClinicalTextProtector : IClinicalTextProtector
{
    private readonly IDiagnosisProtector _diagnosisProtector;
    private readonly IDataProtector _prescriptionProtector;

    /// <summary>Initializes a new instance of the <see cref="ClinicalTextProtector"/> class.</summary>
    /// <param name="diagnosisProtector">The MP-50 diagnosis encryptor, which owns the diagnosis purpose.</param>
    /// <param name="dataProtectionProvider">The application's key ring, used for the prescription purpose.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ClinicalTextProtector(
        IDiagnosisProtector diagnosisProtector,
        IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(diagnosisProtector);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        _diagnosisProtector = diagnosisProtector;
        _prescriptionProtector = dataProtectionProvider.CreateProtector(ClinicalTextPurposeNames.Prescription);
    }

    /// <inheritdoc />
    public string Protect(ClinicalTextPurpose purpose, string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        return purpose switch
        {
            ClinicalTextPurpose.Diagnosis => _diagnosisProtector.Protect(plaintext),
            ClinicalTextPurpose.Prescription => _prescriptionProtector.Protect(plaintext),
            _ => throw Undefined(purpose)
        };
    }

    /// <inheritdoc />
    public string Unprotect(ClinicalTextPurpose purpose, string ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);

        return purpose switch
        {
            ClinicalTextPurpose.Diagnosis => _diagnosisProtector.Unprotect(ciphertext),
            ClinicalTextPurpose.Prescription => _prescriptionProtector.Unprotect(ciphertext),
            _ => throw Undefined(purpose)
        };
    }

    private static ArgumentOutOfRangeException Undefined(ClinicalTextPurpose purpose) =>
        new(nameof(purpose), purpose, "The clinical text purpose is undefined.");
}

namespace MediPassPlus.Application.Privacy;

/// <summary>
/// The kind of clinical text being protected. Each value maps to one versioned Data Protection purpose string;
/// a value is never reused for a different field, so a ciphertext from one purpose cannot be decrypted under
/// another (ADR-0007 decision 5).
/// </summary>
public enum ClinicalTextPurpose
{
    /// <summary>A diagnosis description, stored under <c>MediPass.Diagnosis.v1</c>.</summary>
    Diagnosis = 0,

    /// <summary>
    /// A prescription text field (medicine name, dosage or notes), stored under
    /// <c>MediPass.Prescription.v1</c>.
    /// </summary>
    Prescription = 1
}

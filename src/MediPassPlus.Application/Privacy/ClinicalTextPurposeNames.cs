namespace MediPassPlus.Application.Privacy;

/// <summary>
/// The versioned Data Protection purpose strings the clinical text is encrypted under (ADR-0007 decision 5).
/// Bumping a version is a data migration, not a configuration change.
/// </summary>
public static class ClinicalTextPurposeNames
{
    /// <summary>The purpose of a diagnosis description (ADR-0005).</summary>
    public const string Diagnosis = "MediPass.Diagnosis.v1";

    /// <summary>The purpose of the prescription text fields (ADR-0005).</summary>
    public const string Prescription = "MediPass.Prescription.v1";
}

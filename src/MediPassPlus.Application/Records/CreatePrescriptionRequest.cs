namespace MediPassPlus.Application.Records;

/// <summary>
/// The request body for adding one prescription to an existing diagnosis (FR-REC-04). A diagnosis may have zero or
/// more prescriptions, and each request creates exactly one append-only row (ADR-0005, FR-REC-09).
/// </summary>
public sealed class CreatePrescriptionRequest
{
    /// <summary>The plaintext medicine name; at most 200 characters before encryption (ADR-0005).</summary>
    public string MedicineName { get; set; } = string.Empty;

    /// <summary>The plaintext dosage; at most 200 characters before encryption (ADR-0005).</summary>
    public string Dosage { get; set; } = string.Empty;

    /// <summary>The optional plaintext notes; at most 1,000 characters before encryption (ADR-0005).</summary>
    public string? Notes { get; set; }
}

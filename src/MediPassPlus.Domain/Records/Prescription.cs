namespace MediPassPlus.Domain.Records;

/// <summary>
/// One medicine prescribed for a diagnosis (FR-REC-04, ADR-0005). A diagnosis may have zero or more prescriptions.
/// The row is append-only: a correction is a new row that points at the row it supersedes (FR-REC-09).
/// Every text field holds ciphertext under <c>MediPass.Prescription.v1</c>, never plain text (ADR-0005).
/// </summary>
public sealed class Prescription
{
    private Prescription()
    {
    }

    private Prescription(
        Guid id,
        Guid diagnosisId,
        string medicineNameCiphertext,
        string dosageCiphertext,
        string? notesCiphertext,
        Guid? supersedesPrescriptionId)
    {
        Id = id;
        DiagnosisId = diagnosisId;
        MedicineName = medicineNameCiphertext;
        Dosage = dosageCiphertext;
        Notes = notesCiphertext;
        SupersedesPrescriptionId = supersedesPrescriptionId;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; }

    /// <summary>The diagnosis the prescription belongs to. Required, so a prescription is never orphaned.</summary>
    public Guid DiagnosisId { get; private set; }

    /// <summary>
    /// The earlier prescription this row corrects, if any. The link always stays inside the same diagnosis; when the
    /// diagnosis itself is corrected, the new diagnosis carries its own prescriptions (ADR-0005).
    /// </summary>
    public Guid? SupersedesPrescriptionId { get; private set; }

    /// <summary>
    /// The encrypted medicine name. Encrypted because a clear medicine name would leak the condition the diagnosis
    /// describes; the plaintext 200-character limit is enforced before encryption.
    /// </summary>
    public string MedicineName { get; private set; } = string.Empty;

    /// <summary>The encrypted dosage; the plaintext 200-character limit is enforced before encryption.</summary>
    public string Dosage { get; private set; } = string.Empty;

    /// <summary>The encrypted optional notes; the plaintext 1,000-character limit is enforced before encryption.</summary>
    public string? Notes { get; private set; }

    /// <summary>
    /// Creates an append-only prescription row. The caller supplies the diagnosis and the already encrypted text;
    /// this factory only guards the shape every stored row must have.
    /// </summary>
    /// <param name="diagnosisId">The diagnosis the prescription belongs to.</param>
    /// <param name="medicineNameCiphertext">The encrypted medicine name; never plain text.</param>
    /// <param name="dosageCiphertext">The encrypted dosage; never plain text.</param>
    /// <param name="notesCiphertext">The encrypted optional notes, or <c>null</c> when there are none.</param>
    /// <param name="supersedesPrescriptionId">The earlier prescription this row corrects, when it is a correction.</param>
    /// <returns>The new prescription.</returns>
    /// <exception cref="ArgumentException">An identifier or a required ciphertext is empty.</exception>
    public static Prescription Create(
        Guid diagnosisId,
        string medicineNameCiphertext,
        string dosageCiphertext,
        string? notesCiphertext = null,
        Guid? supersedesPrescriptionId = null)
    {
        if (diagnosisId == Guid.Empty)
        {
            throw new ArgumentException("A diagnosis identifier is required.", nameof(diagnosisId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(medicineNameCiphertext);
        ArgumentException.ThrowIfNullOrWhiteSpace(dosageCiphertext);

        if (supersedesPrescriptionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A superseded prescription identifier cannot be empty.",
                nameof(supersedesPrescriptionId));
        }

        return new Prescription(
            Guid.CreateVersion7(),
            diagnosisId,
            medicineNameCiphertext,
            dosageCiphertext,
            string.IsNullOrWhiteSpace(notesCiphertext) ? null : notesCiphertext,
            supersedesPrescriptionId);
    }
}

namespace MediPassPlus.Domain.Records;

/// <summary>
/// One diagnosis inside a patient's permanent record, always produced by one appointment (FR-REC-03, ADR-0005).
/// The row is append-only: it is never edited or deleted, and a correction is a new row that points at the row it
/// supersedes (FR-REC-09). <see cref="Description"/> holds ciphertext, never plain text (FR-PRV-04, ADR-0007).
/// </summary>
public sealed class Diagnosis
{
    private Diagnosis()
    {
    }

    private Diagnosis(
        Guid id,
        Guid medicalRecordId,
        int appointmentId,
        string descriptionCiphertext,
        DateTime diagnosedAtUtc,
        Guid? supersedesDiagnosisId)
    {
        Id = id;
        MedicalRecordId = medicalRecordId;
        AppointmentId = appointmentId;
        Description = descriptionCiphertext;
        DiagnosedAtUtc = diagnosedAtUtc;
        SupersedesDiagnosisId = supersedesDiagnosisId;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; }

    /// <summary>The record the diagnosis belongs to. Required, so a diagnosis is never orphaned.</summary>
    public Guid MedicalRecordId { get; private set; }

    /// <summary>
    /// The visit that produced the diagnosis. Required, so every diagnosis is visit-scoped (FR-REC-03, US-27). It is
    /// derived server-side from the access grant's appointment, never from the request body.
    /// </summary>
    public int AppointmentId { get; private set; }

    /// <summary>
    /// The earlier diagnosis this row corrects, if any. The corrected row stays visible, and a unique filtered index
    /// prevents two rows from superseding the same diagnosis (ADR-0005).
    /// </summary>
    public Guid? SupersedesDiagnosisId { get; private set; }

    /// <summary>
    /// The encrypted description: the Base64 of the Data Protection payload produced under
    /// <c>MediPass.Diagnosis.v1</c>. The plaintext 4,000-character limit is enforced before encryption; the column is
    /// <c>nvarchar(max)</c> so ciphertext growth never truncates it (ADR-0005, ADR-0007 decision 5).
    /// </summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>When the diagnosis was recorded, in UTC. History is ordered by this column (FR-REC-02).</summary>
    public DateTime DiagnosedAtUtc { get; private set; }

    /// <summary>
    /// Creates an append-only diagnosis row. The caller supplies the medical record, the appointment and the already
    /// encrypted description; this factory only guards the shape every stored row must have.
    /// </summary>
    /// <param name="medicalRecordId">The record the diagnosis belongs to.</param>
    /// <param name="appointmentId">The visit that produced the diagnosis.</param>
    /// <param name="descriptionCiphertext">The encrypted description; never plain text.</param>
    /// <param name="diagnosedAtUtc">When the diagnosis was recorded, in UTC.</param>
    /// <param name="supersedesDiagnosisId">The earlier diagnosis this row corrects, when the row is a correction.</param>
    /// <returns>The new diagnosis.</returns>
    /// <exception cref="ArgumentException">An identifier or the ciphertext is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The appointment identifier is not positive.</exception>
    public static Diagnosis Create(
        Guid medicalRecordId,
        int appointmentId,
        string descriptionCiphertext,
        DateTime diagnosedAtUtc,
        Guid? supersedesDiagnosisId = null)
    {
        if (medicalRecordId == Guid.Empty)
        {
            throw new ArgumentException("A medical record identifier is required.", nameof(medicalRecordId));
        }

        if (appointmentId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appointmentId), "An appointment identifier is required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(descriptionCiphertext);

        if (supersedesDiagnosisId == Guid.Empty)
        {
            throw new ArgumentException(
                "A superseded diagnosis identifier cannot be empty.",
                nameof(supersedesDiagnosisId));
        }

        return new Diagnosis(
            Guid.CreateVersion7(),
            medicalRecordId,
            appointmentId,
            descriptionCiphertext,
            diagnosedAtUtc,
            supersedesDiagnosisId);
    }
}

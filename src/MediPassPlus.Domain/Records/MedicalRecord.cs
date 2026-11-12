namespace MediPassPlus.Domain.Records;

/// <summary>
/// The single permanent record a patient owns (FR-REC-01, ADR-0005). It duplicates no demographics: the Identity
/// account stays the source of truth, and the record only groups the diagnoses and reports that belong to it.
/// The row is created with patient registration, or by the backfill for patients who registered earlier.
/// </summary>
public sealed class MedicalRecord
{
    private MedicalRecord()
    {
    }

    private MedicalRecord(Guid id, Guid patientId, DateTime createdAtUtc)
    {
        Id = id;
        PatientId = patientId;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; }

    /// <summary>The patient's user account identifier. Unique: one record per patient.</summary>
    public Guid PatientId { get; private set; }

    /// <summary>When the record was created, in UTC.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Creates the record of a patient. It is called in the same unit of work as registration, and by the backfill
    /// that covers patients registered before the Record module shipped (ADR-0005).
    /// </summary>
    /// <param name="patientId">The patient's user account identifier.</param>
    /// <param name="createdAtUtc">The current UTC time.</param>
    /// <returns>The new record.</returns>
    /// <exception cref="ArgumentException">The patient identifier is empty.</exception>
    public static MedicalRecord Create(Guid patientId, DateTime createdAtUtc)
    {
        if (patientId == Guid.Empty)
        {
            throw new ArgumentException("A patient identifier is required.", nameof(patientId));
        }

        return new MedicalRecord(Guid.CreateVersion7(), patientId, createdAtUtc);
    }
}

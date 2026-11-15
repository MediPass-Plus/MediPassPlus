using MediPassPlus.Domain.Records;

namespace MediPassPlus.Application.Records;

/// <summary>
/// The unit of work for the diagnosis and prescription write path. The read side resolves the appointment status and
/// the patient's record from the verified grant; the write side stages the append-only row so the new medical row, its
/// audit entry and the save commit together (FR-REC-03, FR-REC-04, ADR-0004 decision 7, ADR-0005).
/// </summary>
public interface IDiagnosisWriteStore
{
    /// <summary>
    /// Loads the appointment status and the patient's permanent record. The appointment must belong to
    /// <paramref name="patientId"/>, so a caller can never load another patient's visit.
    /// </summary>
    /// <param name="appointmentId">The appointment the verified grant is scoped to.</param>
    /// <param name="patientId">The patient taken from the verified grant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The stored write context, or null when no appointment has the identifier for that patient.
    /// </returns>
    Task<DiagnosisWriteContext?> LoadForAppointmentAsync(
        int appointmentId,
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a new diagnosis row in the current unit of work.</summary>
    /// <param name="diagnosis">The diagnosis to add.</param>
    void Add(Diagnosis diagnosis);

    /// <summary>Stages a new prescription row in the current unit of work.</summary>
    /// <param name="prescription">The prescription to add.</param>
    void Add(Prescription prescription);

    /// <summary>Commits the staged medical row and its audit entry in one transaction.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the transaction is committed.</returns>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

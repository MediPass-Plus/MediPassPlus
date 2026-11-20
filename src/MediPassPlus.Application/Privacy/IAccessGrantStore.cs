using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Privacy;

/// <summary>
/// Read-only lookup of the grants that authorize one doctor to read one patient's record: one grant per appointment,
/// or every grant the doctor holds for the patient when the route carries a record instead of a visit. Implemented with
/// EF Core in the Infrastructure layer.
/// </summary>
public interface IAccessGrantStore
{
    /// <summary>Finds the grant scoped to the exact appointment, doctor and patient.</summary>
    /// <param name="appointmentId">The appointment identifier.</param>
    /// <param name="doctorId">The doctor profile identifier taken from the appointment.</param>
    /// <param name="patientId">The patient's user account identifier taken from the appointment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The grant, or null when no grant exists for that scope.</returns>
    Task<AccessGrant?> FindGrantAsync(
        int appointmentId,
        int doctorId,
        Guid patientId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds every grant a doctor holds for one patient, newest first, whatever its current status. The doctor is bound
    /// through their user account, so a caller cannot ask for another doctor's grants, and the lifecycle is left to the
    /// checker (ADR-0011, ADR-0015 decision 2).
    /// </summary>
    /// <param name="doctorUserId">The doctor's user account identifier.</param>
    /// <param name="patientUserId">The patient's user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching grants, newest first; empty when the doctor has none for that patient.</returns>
    Task<IReadOnlyList<AccessGrant>> FindGrantsForDoctorUserAsync(
        Guid doctorUserId,
        Guid patientUserId,
        CancellationToken cancellationToken);
}

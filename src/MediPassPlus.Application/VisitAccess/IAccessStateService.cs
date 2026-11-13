namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Reads one appointment's derived access state for its doctor or its patient. The caller must pass the
/// authenticated user's account identifier; the appointment scope is resolved from the database, never from the
/// request (FR-ACS-06, ADR-0011).
/// </summary>
public interface IAccessStateService
{
    /// <summary>Reads the state for the doctor who owns the appointment.</summary>
    /// <param name="appointmentId">The appointment identifier.</param>
    /// <param name="doctorUserId">The authenticated doctor's user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The derived state or a refusal reason.</returns>
    Task<AccessStateResult> GetForDoctorAsync(
        int appointmentId,
        Guid doctorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the state for the patient who owns the appointment.</summary>
    /// <param name="appointmentId">The appointment identifier.</param>
    /// <param name="patientUserId">The authenticated patient's user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The derived state or a refusal reason.</returns>
    Task<AccessStateResult> GetForPatientAsync(
        int appointmentId,
        Guid patientUserId,
        CancellationToken cancellationToken = default);
}

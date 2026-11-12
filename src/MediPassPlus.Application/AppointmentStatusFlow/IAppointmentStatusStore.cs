namespace MediPassPlus.Application.AppointmentStatusFlow;

/// <summary>
/// The unit of work for the appointment status flow. The read side resolves the appointment, doctor, staff profile
/// and patient; the write side commits the status change and catches a lost concurrency race.
/// </summary>
public interface IAppointmentStatusStore
{
    /// <summary>
    /// Loads the appointment with its doctor and slot, the authenticated staff profile and the patient account.
    /// Returns null only when no appointment has the requested identifier.
    /// </summary>
    /// <param name="appointmentId">The appointment identifier.</param>
    /// <param name="staffUserId">The authenticated staff user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked check-in state, or null when the appointment does not exist.</returns>
    Task<CheckInContext?> LoadForCheckInAsync(
        int appointmentId,
        Guid staffUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the appointment, the authenticated doctor's profile and the appointment's access grant. Returns null only
    /// when no appointment has the requested identifier.
    /// </summary>
    /// <param name="appointmentId">The appointment identifier.</param>
    /// <param name="doctorUserId">The authenticated doctor user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked completion state, or null when the appointment does not exist.</returns>
    Task<CompleteContext?> LoadForCompleteAsync(
        int appointmentId,
        Guid doctorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Commits the staged status change.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="AppointmentStatusConflictException">
    /// Another request changed the appointment status first.
    /// </exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

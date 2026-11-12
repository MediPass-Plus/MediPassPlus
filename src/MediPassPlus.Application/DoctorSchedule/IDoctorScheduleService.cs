namespace MediPassPlus.Application.DoctorSchedule;

/// <summary>
/// Builds a doctor's own schedule for one clinic-local day.
/// </summary>
public interface IDoctorScheduleService
{
    /// <summary>
    /// Returns the authenticated doctor's appointments for one clinic-local date, ordered by start time.
    /// </summary>
    /// <param name="doctorUserId">The authenticated doctor user account identifier.</param>
    /// <param name="date">The clinic-local date to show.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ordered schedule or a refusal reason.</returns>
    Task<DoctorScheduleResult> GetAsync(
        Guid doctorUserId,
        DateOnly date,
        CancellationToken cancellationToken = default);
}

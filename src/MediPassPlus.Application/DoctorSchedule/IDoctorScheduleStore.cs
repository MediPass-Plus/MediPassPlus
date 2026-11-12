using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.DoctorSchedule;

/// <summary>
/// The read side of the doctor schedule. The doctor is resolved from the authenticated user identifier, never from
/// a caller-supplied doctor id, so one doctor cannot read another doctor's schedule (FR-BKG-07).
/// </summary>
public interface IDoctorScheduleStore
{
    /// <summary>Finds the doctor profile linked to a user account.</summary>
    /// <param name="doctorUserId">The authenticated doctor user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The doctor profile, or null when the user has none.</returns>
    Task<Doctor?> FindDoctorByUserIdAsync(Guid doctorUserId, CancellationToken cancellationToken = default);

    /// <summary>Lists the doctor's non-cancelled appointments whose slot starts inside the UTC range.</summary>
    /// <param name="doctorId">The doctor identifier.</param>
    /// <param name="fromUtc">The inclusive start of the range.</param>
    /// <param name="toUtc">The exclusive end of the range.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The appointments ordered by slot start and identifier.</returns>
    Task<IReadOnlyList<DoctorScheduleRow>> ListAsync(
        int doctorId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}

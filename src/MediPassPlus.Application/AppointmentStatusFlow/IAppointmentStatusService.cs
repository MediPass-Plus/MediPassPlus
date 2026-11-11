namespace MediPassPlus.Application.AppointmentStatusFlow;

/// <summary>
/// The reusable appointment status flow. Staff use <see cref="CheckInAsync"/> and doctors use
/// <see cref="CompleteAsync"/>; both delegate the transition rules to the domain entity (FR-BKG-08).
/// </summary>
public interface IAppointmentStatusService
{
    /// <summary>
    /// Marks an appointment of today in the staff member's own clinic as arrived.
    /// </summary>
    /// <param name="appointmentId">The appointment identifier.</param>
    /// <param name="staffUserId">The authenticated staff user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The staff-facing appointment or a refusal reason.</returns>
    Task<CheckInResult> CheckInAsync(
        int appointmentId,
        Guid staffUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes the doctor's own appointment when it is in the visit.
    /// </summary>
    /// <param name="appointmentId">The appointment identifier.</param>
    /// <param name="doctorUserId">The authenticated doctor user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resulting status or a refusal reason.</returns>
    Task<CompleteResult> CompleteAsync(
        int appointmentId,
        Guid doctorUserId,
        CancellationToken cancellationToken = default);
}

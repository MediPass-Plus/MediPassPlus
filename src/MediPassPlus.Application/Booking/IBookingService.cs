namespace MediPassPlus.Application.Booking;

/// <summary>
/// The booking rules shared by every controller: book a free future slot, cancel a booked appointment, and reschedule
/// it to another free slot of the same doctor (FR-BKG-03, FR-BKG-04, FR-BKG-05, FR-BKG-09, NFR-REL-03).
/// </summary>
public interface IBookingService
{
    /// <summary>
    /// Books a free, future slot for the authenticated patient and marks the slot unavailable in the same unit of work.
    /// </summary>
    /// <param name="doctorId">The doctor the patient wants to book with.</param>
    /// <param name="slotId">The slot the patient wants to book.</param>
    /// <param name="patientUserId">The authenticated patient's user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The booked appointment or a refusal reason.</returns>
    Task<BookingResult> BookAsync(
        int doctorId,
        int slotId,
        Guid patientUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels one of the authenticated patient's booked appointments before it starts and frees its slot.
    /// </summary>
    /// <param name="appointmentId">The appointment identifier.</param>
    /// <param name="patientUserId">The authenticated patient's user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cancelled appointment or a refusal reason.</returns>
    Task<BookingResult> CancelAsync(
        int appointmentId,
        Guid patientUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves one of the authenticated patient's booked appointments to another free slot of the same doctor, before
    /// the current slot starts.
    /// </summary>
    /// <param name="appointmentId">The appointment identifier.</param>
    /// <param name="newSlotId">The target slot identifier.</param>
    /// <param name="patientUserId">The authenticated patient's user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rescheduled appointment or a refusal reason.</returns>
    Task<BookingResult> RescheduleAsync(
        int appointmentId,
        int newSlotId,
        Guid patientUserId,
        CancellationToken cancellationToken = default);
}

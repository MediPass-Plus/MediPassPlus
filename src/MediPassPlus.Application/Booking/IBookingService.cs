using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.Booking;

/// <summary>
/// The booking rules shared by every controller: book a free future slot, cancel a booked appointment, reschedule it
/// to another free slot of the same doctor, and list the authenticated patient's own appointments
/// (FR-BKG-03, FR-BKG-04, FR-BKG-05, FR-BKG-06, FR-BKG-09, NFR-REL-03).
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

    /// <summary>
    /// Returns one page of the authenticated patient's own appointments, newest slot first, with the total count.
    /// The patient identifier comes from the caller's token, never from the request (FR-BKG-06, NFR-PRF-04).
    /// </summary>
    /// <param name="patientUserId">The authenticated patient's user account identifier.</param>
    /// <param name="page">The requested page and page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested page of appointments.</returns>
    Task<PagedResult<PatientAppointmentDto>> GetMyAppointmentsAsync(
        Guid patientUserId,
        PageRequest page,
        CancellationToken cancellationToken = default);
}

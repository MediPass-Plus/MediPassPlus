using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.Booking;

/// <summary>
/// The unit of work for the booking rules. The read side resolves the tracked slot or appointment inside the
/// authenticated patient's scope; the write side stages the appointment and commits the slot and appointment changes
/// together.
/// </summary>
public interface IBookingStore
{
    /// <summary>
    /// Loads the slot requested for booking, the doctor requested by the caller, both user accounts and whether the
    /// slot already has an active appointment. Returns null only when no slot has the requested identifier.
    /// </summary>
    /// <param name="slotId">The slot the caller wants to book.</param>
    /// <param name="doctorId">The doctor the caller wants to book with.</param>
    /// <param name="patientUserId">The authenticated patient's user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked booking state, or null when the slot does not exist.</returns>
    Task<BookingSlotContext?> LoadBookingSlotAsync(
        int slotId,
        int doctorId,
        Guid patientUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the appointment only when it belongs to the authenticated patient. The appointment and its current slot
    /// are tracked so a cancel or reschedule can change them in one unit of work.
    /// </summary>
    /// <param name="appointmentId">The appointment identifier.</param>
    /// <param name="patientUserId">The authenticated patient's user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked appointment state, or null when the appointment does not exist in this patient's scope.</returns>
    Task<BookingAppointmentContext?> LoadAppointmentAsync(
        int appointmentId,
        Guid patientUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the target slot for a reschedule and whether another active appointment already uses it. Returns null
    /// when no slot has the requested identifier.
    /// </summary>
    /// <param name="slotId">The target slot identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked target slot, or null when the slot does not exist.</returns>
    Task<TargetSlotContext?> LoadTargetSlotAsync(
        int slotId,
        CancellationToken cancellationToken = default);

    /// <summary>Stages a new appointment.</summary>
    /// <param name="appointment">The appointment to add.</param>
    void AddAppointment(Appointment appointment);

    /// <summary>Commits the staged appointment and slot changes in one transaction.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="BookingConflictException">
    /// A concurrent request booked the slot first, or another request changed the appointment or slot state.
    /// </exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

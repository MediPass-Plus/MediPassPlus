namespace MediPassPlus.Application.Booking;

/// <summary>
/// The request body for rescheduling an appointment. The doctor and patient are taken from the appointment, never
/// from the request, so a caller cannot move an appointment into another scope.
/// </summary>
public sealed class RescheduleAppointmentRequest
{
    /// <summary>The target slot identifier.</summary>
    public int NewSlotId { get; set; }
}

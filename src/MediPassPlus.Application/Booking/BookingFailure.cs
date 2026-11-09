namespace MediPassPlus.Application.Booking;

/// <summary>
/// Why a booking, cancellation or reschedule was refused. The API maps the not-found failures to <c>404</c>, the
/// invalid-state and inactive-actor failures to <c>400</c>, and the booking conflicts to <c>409</c>.
/// </summary>
public enum BookingFailure
{
    /// <summary>The operation succeeded.</summary>
    None = 0,

    /// <summary>No doctor has the requested identifier.</summary>
    DoctorNotFound,

    /// <summary>The authenticated patient account no longer exists.</summary>
    PatientNotFound,

    /// <summary>No slot has the requested identifier.</summary>
    SlotNotFound,

    /// <summary>No appointment with the requested identifier belongs to the authenticated patient.</summary>
    AppointmentNotFound,

    /// <summary>The doctor's account is inactive.</summary>
    DoctorInactive,

    /// <summary>The patient's account is inactive.</summary>
    PatientInactive,

    /// <summary>The slot starts in the past.</summary>
    SlotInPast,

    /// <summary>The slot belongs to another doctor.</summary>
    SlotDoctorMismatch,

    /// <summary>The slot is unavailable or already has an active appointment.</summary>
    SlotAlreadyBooked,

    /// <summary>The appointment is not in a state that allows cancellation.</summary>
    AppointmentNotCancellable,

    /// <summary>The appointment is not in a state that allows rescheduling.</summary>
    AppointmentNotReschedulable,

    /// <summary>The appointment's current slot has already started.</summary>
    AppointmentAlreadyStarted,

    /// <summary>The reschedule target is the slot the appointment already occupies.</summary>
    AppointmentAlreadyOnSlot,

    /// <summary>A concurrent request changed the appointment or the slot first.</summary>
    AppointmentConflict
}

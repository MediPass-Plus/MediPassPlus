namespace MediPassPlus.Application.AppointmentStatusFlow;

/// <summary>
/// Why a staff check-in was refused. The API maps the not-found failures to <c>404</c>, the authorization and
/// inactive-state failures to <c>403</c>, the invalid-state failures to <c>400</c>, and the conflict to <c>409</c>.
/// </summary>
public enum CheckInFailure
{
    /// <summary>The check-in succeeded.</summary>
    None = 0,

    /// <summary>No appointment has the requested identifier.</summary>
    AppointmentNotFound,

    /// <summary>The authenticated user has no clinic-staff profile.</summary>
    StaffNotFound,

    /// <summary>The appointment belongs to another clinic.</summary>
    ClinicMismatch,

    /// <summary>The appointment's slot is not on today's clinic-local date.</summary>
    AppointmentNotToday,

    /// <summary>The appointment is not in the <c>Booked</c> state.</summary>
    AppointmentNotBooked,

    /// <summary>The appointment's patient account no longer exists.</summary>
    PatientNotFound,

    /// <summary>A concurrent request changed the appointment status first.</summary>
    AppointmentConflict
}

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Why a start-visit request was refused. The API maps <see cref="AppointmentNotFound"/> to <c>404</c>, the doctor
/// failures to <c>403</c>, the appointment-state failures to <c>400</c>, and <see cref="RequestConflict"/> to
/// <c>409</c>.
/// </summary>
public enum StartVisitFailure
{
    /// <summary>The visit started successfully.</summary>
    None = 0,

    /// <summary>No appointment has the requested identifier.</summary>
    AppointmentNotFound,

    /// <summary>The authenticated user has no doctor profile.</summary>
    DoctorNotFound,

    /// <summary>The appointment belongs to another doctor.</summary>
    DoctorMismatch,

    /// <summary>The appointment is not scheduled for today.</summary>
    AppointmentNotToday,

    /// <summary>The appointment is not in the <c>CheckedIn</c> state.</summary>
    AppointmentNotCheckedIn,

    /// <summary>
    /// A concurrent call created the appointment's pending request first; retrying reuses that request.
    /// </summary>
    RequestConflict
}

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Why a start-visit request was refused. The API maps <see cref="AppointmentNotFound"/> to <c>404</c>, the doctor
/// failures to <c>403</c>, the appointment-state failures to <c>400</c>, and <see cref="AccessAlreadyGranted"/> and
/// <see cref="RequestConflict"/> to <c>409</c>.
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

    /// <summary>The appointment is not in a startable state (<c>CheckedIn</c> or <c>InVisit</c>).</summary>
    AppointmentNotCheckedIn,

    /// <summary>
    /// The patient already approved a request for this appointment, so a grant exists. An ended grant is never
    /// reused and only one grant is allowed per appointment (FR-ACS-11), so issuing another QR would create a
    /// request that can never be approved.
    /// </summary>
    AccessAlreadyGranted,

    /// <summary>
    /// A concurrent call won the pending-request or live-credential race; retrying reuses the pending request.
    /// </summary>
    RequestConflict
}

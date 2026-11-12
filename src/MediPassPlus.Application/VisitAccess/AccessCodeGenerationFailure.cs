namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Why a six-digit code request was refused. The API maps <see cref="AppointmentNotFound"/> to <c>404</c>,
/// <see cref="PatientMismatch"/> to <c>403</c>, the appointment-state failures to <c>400</c>, and the request failures
/// to <c>409</c>.
/// </summary>
public enum AccessCodeGenerationFailure
{
    /// <summary>The code was generated.</summary>
    None = 0,

    /// <summary>No appointment has the requested identifier.</summary>
    AppointmentNotFound,

    /// <summary>The authenticated patient does not own the appointment.</summary>
    PatientMismatch,

    /// <summary>The appointment is not in a startable state (<c>CheckedIn</c> or <c>InVisit</c>).</summary>
    AppointmentNotCheckedIn,

    /// <summary>The appointment is not scheduled for today in the clinic calendar.</summary>
    AppointmentNotToday,

    /// <summary>The patient already approved a request, so the visit's grant exists.</summary>
    AccessAlreadyGranted,

    /// <summary>The visit has no pending request; the doctor must start the visit first.</summary>
    RequestNotPending,

    /// <summary>A concurrent call won the code-replacement race.</summary>
    RequestConflict
}

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Why a six-digit code verification was refused. The API maps <see cref="AppointmentNotFound"/> to <c>404</c>, the
/// doctor failures to <c>403</c>, <see cref="AppointmentNotCheckedIn"/> and <see cref="InvalidCode"/> to <c>400</c>,
/// and the remaining failures to <c>409</c>.
/// </summary>
public enum AccessCodeVerificationFailure
{
    /// <summary>The code matched and the grant was created.</summary>
    None = 0,

    /// <summary>No appointment has the requested identifier.</summary>
    AppointmentNotFound,

    /// <summary>The authenticated user has no doctor profile.</summary>
    DoctorNotFound,

    /// <summary>The appointment belongs to another doctor.</summary>
    DoctorMismatch,

    /// <summary>The appointment is not in a verifiable state (<c>CheckedIn</c> or <c>InVisit</c>).</summary>
    AppointmentNotCheckedIn,

    /// <summary>The patient already approved a request, so the visit's grant exists.</summary>
    AccessAlreadyGranted,

    /// <summary>The visit has no pending request; the doctor must start the visit first.</summary>
    RequestNotPending,

    /// <summary>No usable code exists; it is missing, expired, consumed or revoked.</summary>
    CodeUnavailable,

    /// <summary>The code was locked after five wrong entries (FR-ACS-05).</summary>
    CodeLocked,

    /// <summary>The presented code did not match; the attempt was recorded.</summary>
    InvalidCode,

    /// <summary>A concurrent call changed the appointment, request or grant first.</summary>
    RequestConflict
}

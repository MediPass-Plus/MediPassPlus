namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Why a visit-access scope check was refused. Callers map <see cref="AppointmentNotFound"/> to <c>404</c> and every
/// other value to <c>403</c>; the distinction never exposes appointment contents.
/// </summary>
public enum VisitAccessScopeFailure
{
    /// <summary>The check succeeded.</summary>
    None = 0,

    /// <summary>No appointment has the requested identifier.</summary>
    AppointmentNotFound,

    /// <summary>The authenticated user has no doctor profile.</summary>
    DoctorNotFound,

    /// <summary>The appointment belongs to another doctor.</summary>
    DoctorMismatch,

    /// <summary>The appointment belongs to another patient.</summary>
    PatientMismatch
}

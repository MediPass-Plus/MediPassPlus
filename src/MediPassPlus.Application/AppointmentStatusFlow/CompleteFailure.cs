namespace MediPassPlus.Application.AppointmentStatusFlow;

/// <summary>
/// Why a doctor completion was refused. The API maps the not-found failures to <c>404</c>, the doctor failures to
/// <c>403</c>, the invalid-state failure to <c>400</c>, and the conflict to <c>409</c>.
/// </summary>
public enum CompleteFailure
{
    /// <summary>The completion succeeded.</summary>
    None = 0,

    /// <summary>No appointment has the requested identifier.</summary>
    AppointmentNotFound,

    /// <summary>The authenticated user has no doctor profile.</summary>
    DoctorNotFound,

    /// <summary>The appointment belongs to another doctor.</summary>
    DoctorMismatch,

    /// <summary>The appointment is not in the <c>InVisit</c> state.</summary>
    AppointmentNotInVisit,

    /// <summary>A concurrent request changed the appointment status first.</summary>
    AppointmentConflict
}

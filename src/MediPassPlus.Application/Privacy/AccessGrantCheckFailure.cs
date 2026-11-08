namespace MediPassPlus.Application.Privacy;

/// <summary>
/// Why an access-grant check was refused. The API maps every value except <see cref="None"/> to the same
/// <c>403</c> response, so a caller cannot learn whether the appointment exists (FR-PRV-01, NFR-PRI-01).
/// </summary>
public enum AccessGrantCheckFailure
{
    /// <summary>The check succeeded.</summary>
    None = 0,

    /// <summary>The authenticated user has no doctor profile.</summary>
    DoctorNotFound,

    /// <summary>No appointment has the requested identifier.</summary>
    AppointmentNotFound,

    /// <summary>The appointment belongs to another doctor.</summary>
    DoctorMismatch,

    /// <summary>The appointment has no grant for this doctor and patient.</summary>
    GrantNotFound,

    /// <summary>The grant was ended by visit completion or by the patient.</summary>
    GrantEnded,

    /// <summary>The grant reached its two-hour limit.</summary>
    GrantExpired,

    /// <summary>The stored grant does not match the appointment's doctor and patient.</summary>
    ScopeMismatch
}

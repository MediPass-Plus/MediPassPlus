namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Why an access-state read was refused. The API maps <see cref="AppointmentNotFound"/> to <c>404</c> and
/// <see cref="ActorMismatch"/> to <c>403</c>.
/// </summary>
public enum AccessStateFailure
{
    /// <summary>The read succeeded.</summary>
    None = 0,

    /// <summary>No appointment has the requested identifier.</summary>
    AppointmentNotFound,

    /// <summary>The authenticated doctor or patient does not own the appointment.</summary>
    ActorMismatch
}

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Read-only lookup for the access-state endpoint. The appointment scope is always checked against the stored
/// appointment before the request and grant are read (FR-ACS-06, ADR-0011 decision 8).
/// </summary>
public interface IAccessStateStore
{
    /// <summary>
    /// Loads the newest request and the grant for a verified appointment scope.
    /// </summary>
    /// <param name="appointmentId">The appointment identifier.</param>
    /// <param name="doctorId">The appointment's doctor identifier.</param>
    /// <param name="patientId">The appointment's patient user identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The state, or null when the appointment no longer matches the scope.</returns>
    Task<AccessStateContext?> LoadAsync(
        int appointmentId,
        int doctorId,
        Guid patientId,
        CancellationToken cancellationToken = default);
}

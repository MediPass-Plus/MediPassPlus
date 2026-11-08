using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Privacy;

/// <summary>
/// Read-only lookup of the grant that authorizes one doctor to read one patient's record for one appointment.
/// Implemented with EF Core in the Infrastructure layer.
/// </summary>
public interface IAccessGrantStore
{
    /// <summary>Finds the grant scoped to the exact appointment, doctor and patient.</summary>
    /// <param name="appointmentId">The appointment identifier.</param>
    /// <param name="doctorId">The doctor profile identifier taken from the appointment.</param>
    /// <param name="patientId">The patient's user account identifier taken from the appointment.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The grant, or null when no grant exists for that scope.</returns>
    Task<AccessGrant?> FindGrantAsync(
        int appointmentId,
        int doctorId,
        Guid patientId,
        CancellationToken cancellationToken);
}

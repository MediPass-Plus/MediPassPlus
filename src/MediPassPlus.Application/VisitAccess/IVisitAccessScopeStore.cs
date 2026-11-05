using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Read-only lookups needed to verify a visit-access scope. Implemented with EF Core in the Infrastructure layer.
/// </summary>
public interface IVisitAccessScopeStore
{
    /// <summary>Finds an appointment by its primary key.</summary>
    /// <param name="appointmentId">The appointment identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Appointment?> FindAppointmentAsync(int appointmentId, CancellationToken cancellationToken);

    /// <summary>Finds the doctor profile linked to a user account. The link is unique.</summary>
    /// <param name="userId">The user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<Doctor?> FindDoctorByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}

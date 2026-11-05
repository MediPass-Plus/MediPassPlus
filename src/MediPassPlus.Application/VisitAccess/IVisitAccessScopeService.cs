namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Verifies that the authenticated actor really belongs to the appointment before visit access is created or used.
/// </summary>
public interface IVisitAccessScopeService
{
    /// <summary>Verifies that the authenticated doctor owns the appointment (FR-ACS-01, FR-ACS-10).</summary>
    /// <param name="appointmentId">The appointment being started.</param>
    /// <param name="doctorUserId">The authenticated doctor's user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<VisitAccessScopeResult> ResolveForDoctorAsync(
        int appointmentId,
        Guid doctorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Verifies that the authenticated patient owns the appointment (FR-ACS-03, FR-ACS-06).</summary>
    /// <param name="appointmentId">The appointment being approved.</param>
    /// <param name="patientUserId">The authenticated patient's user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<VisitAccessScopeResult> ResolveForPatientAsync(
        int appointmentId,
        Guid patientUserId,
        CancellationToken cancellationToken = default);
}

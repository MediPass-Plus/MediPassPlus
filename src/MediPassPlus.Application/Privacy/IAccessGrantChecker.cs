namespace MediPassPlus.Application.Privacy;

/// <summary>
/// The single gate every medical endpoint must pass before it reads or writes a patient's record. The check runs
/// against the database on each request; a token claim is never proof of access (FR-PRV-01, FR-ACS-06, NFR-PRI-01).
/// </summary>
public interface IAccessGrantChecker
{
    /// <summary>
    /// Verifies that <paramref name="doctorUserId"/> owns the appointment and holds an active grant for it.
    /// The patient and the doctor profile are resolved from the appointment, never from caller-supplied data.
    /// </summary>
    /// <param name="doctorUserId">The authenticated doctor's user account identifier.</param>
    /// <param name="appointmentId">The appointment the request is scoped to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The active grant, or the reason the check was refused.</returns>
    Task<AccessGrantCheckResult> RequireActiveGrantAsync(
        Guid doctorUserId,
        int appointmentId,
        CancellationToken cancellationToken = default);
}

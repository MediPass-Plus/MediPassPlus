namespace MediPassPlus.Application.VisitAccess;

/// <summary>Starts a doctor's visit and issues a fresh one-time QR credential (FR-ACS-01, FR-ACS-02).</summary>
public interface IStartVisitService
{
    /// <summary>
    /// Starts the visit for the authenticated doctor. The caller must pass the authenticated user account id, never a
    /// doctor id supplied by the client (FR-ACS-06, ADR-0011 decision 8).
    /// </summary>
    /// <param name="appointmentId">The appointment being started.</param>
    /// <param name="doctorUserId">The authenticated doctor's user account identifier.</param>
    /// <param name="ipAddress">
    /// The connection IP address recorded on the request-creation audit row (ADR-0004 decision 8).
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The one-time QR response or a refusal reason.</returns>
    Task<StartVisitResult> StartAsync(
        int appointmentId,
        Guid doctorUserId,
        string ipAddress,
        CancellationToken cancellationToken = default);
}

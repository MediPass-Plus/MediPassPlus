namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Generates the six-digit fallback for a pending request on behalf of its patient (FR-ACS-05, ADR-0002). The caller
/// must pass the authenticated patient's user account identifier, never an identifier supplied by the client.
/// </summary>
public interface IAccessCodeGenerationService
{
    /// <summary>Generates a new code, replacing every live code for the appointment's pending request.</summary>
    /// <param name="appointmentId">The appointment the code is generated for.</param>
    /// <param name="patientUserId">The authenticated patient's user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The one-time code response or a refusal reason.</returns>
    Task<AccessCodeGenerationResult> GenerateAsync(
        int appointmentId,
        Guid patientUserId,
        CancellationToken cancellationToken = default);
}

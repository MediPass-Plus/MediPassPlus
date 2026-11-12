namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Verifies the patient's six-digit fallback on behalf of the doctor who owns the appointment (ADR-0002). A match is
/// the patient's approval expressed out of band: it creates the visit's grant and consumes the code
/// (ADR-0011 amendment 2026-11-12).
/// </summary>
public interface IAccessCodeVerificationService
{
    /// <summary>
    /// Verifies a presented code. A wrong code records a failed attempt and locks the credential on the fifth one; a
    /// match creates the grant. The caller must pass the authenticated doctor's user account identifier.
    /// </summary>
    /// <param name="appointmentId">The appointment the code was generated for.</param>
    /// <param name="doctorUserId">The authenticated doctor's user account identifier.</param>
    /// <param name="code">The plaintext six-digit code.</param>
    /// <param name="ipAddress">The connection IP address recorded on the grant-creation audit row.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created grant or a refusal reason.</returns>
    Task<AccessCodeVerificationResult> VerifyAsync(
        int appointmentId,
        Guid doctorUserId,
        string code,
        string ipAddress,
        CancellationToken cancellationToken = default);
}

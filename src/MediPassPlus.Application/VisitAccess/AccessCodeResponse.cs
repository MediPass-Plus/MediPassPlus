namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The one-time result of generating the six-digit fallback. The plaintext code is the only secret in this contract
/// and must never be logged or returned again.
/// </summary>
/// <param name="AppointmentId">The appointment the code was generated for.</param>
/// <param name="AccessRequestId">The pending request the code proves.</param>
/// <param name="Code">The plaintext six-digit code. Only its HMAC is stored.</param>
/// <param name="ExpiresAtUtc">When the code stops being valid, in UTC (five minutes after creation).</param>
public sealed record AccessCodeResponse(
    int AppointmentId,
    Guid AccessRequestId,
    string Code,
    DateTime ExpiresAtUtc);

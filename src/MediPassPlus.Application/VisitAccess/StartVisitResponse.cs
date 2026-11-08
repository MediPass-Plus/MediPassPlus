namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The one-time result of starting a visit. The QR token is the only secret in this contract and must never be
/// logged or returned again.
/// </summary>
/// <param name="AppointmentId">The appointment that was started.</param>
/// <param name="AccessRequestId">The pending access request the token proves.</param>
/// <param name="QrToken">The plaintext Base64Url QR token. Only its SHA-256 hash is stored.</param>
/// <param name="ExpiresAtUtc">When the token stops being valid, in UTC (two minutes after creation).</param>
public sealed record StartVisitResponse(
    int AppointmentId,
    Guid AccessRequestId,
    string QrToken,
    DateTime ExpiresAtUtc);

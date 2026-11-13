namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The QR token a patient presents after scanning the doctor's code. The token travels in the request body, never
/// in the URL, so it is not written to access logs.
/// </summary>
/// <param name="Token">The plaintext Base64Url QR token returned by start-visit.</param>
public sealed record QrTokenRequest(string Token);

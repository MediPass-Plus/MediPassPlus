namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The six-digit code the owning doctor enters for a visit. The code travels in the request body, never in the URL,
/// so it is not written to access logs.
/// </summary>
/// <param name="Code">The six ASCII digits the patient read out.</param>
public sealed record VerifyAccessCodeRequest(string Code);

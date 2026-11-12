namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The grant a patient ended immediately. It carries only the grant identifier and the stored end, so the response
/// stays minimal and never exposes medical data.
/// </summary>
/// <param name="GrantId">The grant identifier.</param>
/// <param name="EndedAtUtc">When the grant ended, in UTC.</param>
/// <param name="EndReason">The stable end reason name; <c>PatientEnded</c> for this path.</param>
public sealed record AccessGrantEndedResponse(
    Guid GrantId,
    DateTime EndedAtUtc,
    string EndReason);

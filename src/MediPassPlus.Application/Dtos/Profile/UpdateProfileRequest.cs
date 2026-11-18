namespace MediPassPlus.Application.Dtos.Profile;

/// <summary>
/// The only fields a user may change about themselves (FR-ACC-07, US-03). There is deliberately no id, email,
/// role or status here: the account is resolved from the access token, so the body cannot choose whose profile
/// is written.
/// </summary>
/// <param name="FullName">The new display name.</param>
/// <param name="PhoneNumber">The new phone number, or <c>null</c>/empty to clear it.</param>
public sealed record UpdateProfileRequest(string FullName, string? PhoneNumber);

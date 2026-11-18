namespace MediPassPlus.Application.Dtos.Profile;

/// <summary>
/// The authenticated user's own profile (FR-ACC-07, US-03). <c>Email</c> and <c>Role</c> are read-only: they are
/// returned so the client can show them, but the update endpoint never accepts them.
/// </summary>
/// <param name="FullName">The display name.</param>
/// <param name="PhoneNumber">The stored phone number, or <c>null</c> when the user cleared it.</param>
/// <param name="Email">The account email, read-only.</param>
/// <param name="Role">The account role, read-only.</param>
public sealed record ProfileResponse(
    string FullName,
    string? PhoneNumber,
    string Email,
    string Role);

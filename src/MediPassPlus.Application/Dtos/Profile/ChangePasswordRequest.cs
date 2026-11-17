namespace MediPassPlus.Application.Dtos.Profile;

/// <summary>The body of <c>POST api/me/profile/change-password</c> (FR-ACC-08, US-04).</summary>
/// <param name="CurrentPassword">The password in use now; a wrong value refuses the change.</param>
/// <param name="NewPassword">The replacement password; it must satisfy the account password policy.</param>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

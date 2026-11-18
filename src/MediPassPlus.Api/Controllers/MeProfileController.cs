using MediPassPlus.Api.RateLimiting;
using MediPassPlus.Application.Auth;
using MediPassPlus.Application.Dtos.Profile;
using MediPassPlus.Application.Identity;
using MediPassPlus.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediPassPlus.Api.Controllers;

/// <summary>
/// The self-service account surface: every registered user reads and edits their own profile and changes their own
/// password (FR-ACC-07, FR-ACC-08, US-03, US-04, UC-03). The account is resolved from the access token, so no route
/// takes an identifier and one user can never reach another user's profile.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public class MeProfileController : ControllerBase
{
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string PasswordMismatchCode = "PasswordMismatch";

    private readonly UserManager<ApplicationUser> _users;
    private readonly ICurrentUser _currentUser;
    private readonly IAuthTokenService _authTokens;

    /// <summary>Initializes a new instance of the <see cref="MeProfileController"/> class.</summary>
    /// <param name="users">The Identity user manager.</param>
    /// <param name="currentUser">The authenticated caller of this request.</param>
    /// <param name="authTokens">Ends the account's live sessions after a password change.</param>
    public MeProfileController(
        UserManager<ApplicationUser> users,
        ICurrentUser currentUser,
        IAuthTokenService authTokens)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(authTokens);

        _users = users;
        _currentUser = currentUser;
        _authTokens = authTokens;
    }

    /// <summary>Returns the authenticated user's own profile.</summary>
    /// <returns>The profile of the caller.</returns>
    /// <response code="200">The caller's profile.</response>
    /// <response code="401">The access token is missing, invalid or expired, or it does not identify a user.</response>
    [HttpGet("profile")]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetProfile()
    {
        var user = await FindCurrentUserAsync();

        return user is null
            ? CreateProblem(
                StatusCodes.Status401Unauthorized,
                UnauthorizedType,
                "Authentication required.",
                "The access token does not identify a user.")
            : Ok(await ToResponseAsync(user));
    }

    /// <summary>
    /// Updates the caller's name and phone number. Email, role and account status are not editable here
    /// (FR-ACC-07, US-03).
    /// </summary>
    /// <param name="request">The new name and phone number.</param>
    /// <returns>The profile as stored after the update.</returns>
    /// <response code="200">The profile was saved and is returned as stored.</response>
    /// <response code="400">
    /// The request is refused and the body is a <see cref="ProblemDetails"/>. A body that breaks a validation rule is a
    /// <see cref="ValidationProblemDetails"/> whose <c>errors</c> bag names the offending member (for example
    /// <c>FullName</c> or <c>PhoneNumber</c>); a body Identity refuses carries the refusal in <c>detail</c> and has no
    /// <c>errors</c> bag.
    /// </response>
    /// <response code="401">The access token is missing, invalid or expired, or it does not identify a user.</response>
    [HttpPut("profile")]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request)
    {
        var user = await FindCurrentUserAsync();

        if (user is null)
        {
            return CreateProblem(
                StatusCodes.Status401Unauthorized,
                UnauthorizedType,
                "Authentication required.",
                "The access token does not identify a user.");
        }

        var phoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
            ? null
            : request.PhoneNumber.Trim();

        // A changed phone number is unconfirmed again, so a later verification flow cannot inherit the old state.
        if (!string.Equals(user.PhoneNumber, phoneNumber, StringComparison.Ordinal))
        {
            user.PhoneNumber = phoneNumber;
            user.PhoneNumberConfirmed = false;
        }

        user.FullName = request.FullName.Trim();

        var result = await _users.UpdateAsync(user);

        if (!result.Succeeded)
        {
            return CreateProblem(
                StatusCodes.Status400BadRequest,
                "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                "The profile could not be saved.",
                string.Join(" ", result.Errors.Select(error => error.Description)));
        }

        return Ok(await ToResponseAsync(user));
    }

    /// <summary>
    /// Changes the caller's password after checking the current one and then ends every live session of the account,
    /// so a refresh token stolen before the change cannot outlive it (FR-ACC-06, FR-ACC-08, US-04).
    /// </summary>
    /// <param name="request">The current password and the new one.</param>
    /// <returns>A message confirming the change.</returns>
    /// <response code="200">
    /// The current password was correct, the new one satisfied the password policy, and every refresh token of the
    /// account was revoked. The access token of this request keeps working until it expires; after that the account
    /// signs in again with the new password.
    /// </response>
    /// <response code="400">
    /// The request is refused and the body is a <see cref="ProblemDetails"/>. A body that breaks a validation rule is a
    /// <see cref="ValidationProblemDetails"/> whose <c>errors</c> bag names the offending member (for example
    /// <c>NewPassword</c>); a wrong current password or an Identity refusal carries the reason in <c>detail</c> and has
    /// no <c>errors</c> bag. A wrong current password is a failure of this request, not of authentication: the caller is
    /// already authenticated. No session is revoked when the request is refused.
    /// </response>
    /// <response code="401">The access token is missing, invalid or expired, or it does not identify a user.</response>
    /// <response code="429">
    /// The account exhausted its password-change budget (5 attempts per 15 minutes by default) and every attempt
    /// shares it, successful or not. The response carries <c>Retry-After</c>.
    /// </response>
    [HttpPost("change-password")]
    [EnableRateLimiting(ChangePasswordRateLimitPolicy.Name)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await FindCurrentUserAsync();

        if (user is null)
        {
            return CreateProblem(
                StatusCodes.Status401Unauthorized,
                UnauthorizedType,
                "Authentication required.",
                "The access token does not identify a user.");
        }

        var result = await _users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
            // A wrong current password is reported as one neutral message; the account exists either way.
            var detail = result.Errors.Any(error => error.Code == PasswordMismatchCode)
                ? "The current password is incorrect."
                : string.Join(" ", result.Errors.Select(error => error.Description));

            return CreateProblem(
                StatusCodes.Status400BadRequest,
                "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                "The password could not be changed.",
                detail);
        }

        // A token that leaked before the change must not outlive it (FR-ACC-06, FR-ACC-08).
        await _authTokens.RevokeAllForUserAsync(user.Id, HttpContext.RequestAborted);

        return Ok(new { message = "Password changed successfully." });
    }

    private async Task<ApplicationUser?> FindCurrentUserAsync() =>
        _currentUser.UserId is { } userId
            ? await _users.FindByIdAsync(userId.ToString())
            : null;

    private async Task<ProfileResponse> ToResponseAsync(ApplicationUser user)
    {
        // The role comes from Identity, not from the duplicate string column on the user, so a stale copy
        // cannot be shown here.
        var roles = await _users.GetRolesAsync(user);

        return new ProfileResponse(
            user.FullName,
            user.PhoneNumber,
            user.Email ?? string.Empty,
            roles.FirstOrDefault() ?? string.Empty);
    }

    private ObjectResult CreateProblem(int statusCode, string type, string title, string detail) =>
        Problem(statusCode: statusCode, type: type, title: title, detail: detail);
}

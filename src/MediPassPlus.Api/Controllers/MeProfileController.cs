using MediPassPlus.Application.Dtos.Profile;
using MediPassPlus.Application.Identity;
using MediPassPlus.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

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

    /// <summary>Initializes a new instance of the <see cref="MeProfileController"/> class.</summary>
    /// <param name="users">The Identity user manager.</param>
    /// <param name="currentUser">The authenticated caller of this request.</param>
    public MeProfileController(UserManager<ApplicationUser> users, ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(currentUser);

        _users = users;
        _currentUser = currentUser;
    }

    /// <summary>Returns the authenticated user's own profile.</summary>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The profile of the caller.</returns>
    /// <response code="200">The caller's profile.</response>
    /// <response code="401">The access token is missing, invalid or expired, or it does not identify a user.</response>
    [HttpGet("profile")]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var user = await FindCurrentUserAsync(cancellationToken);

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
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>The profile as stored after the update.</returns>
    /// <response code="200">The profile was saved and is returned as stored.</response>
    /// <response code="400">The body breaks a validation rule, or Identity refused the update.</response>
    /// <response code="401">The access token is missing, invalid or expired, or it does not identify a user.</response>
    [HttpPut("profile")]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateProfile(
        UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var user = await FindCurrentUserAsync(cancellationToken);

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
    /// Changes the caller's password after checking the current one (FR-ACC-08, US-04).
    /// </summary>
    /// <param name="request">The current password and the new one.</param>
    /// <param name="cancellationToken">Cancels the database work.</param>
    /// <returns>A message confirming the change.</returns>
    /// <response code="200">The current password was correct and the new one satisfied the password policy.</response>
    /// <response code="400">
    /// The body breaks a validation rule, the current password is wrong, or Identity refused the new password.
    /// A wrong current password is a failure of this request, not of authentication: the caller is already
    /// authenticated.
    /// </response>
    /// <response code="401">The access token is missing, invalid or expired, or it does not identify a user.</response>
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var user = await FindCurrentUserAsync(cancellationToken);

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

        return Ok(new { message = "Password changed successfully." });
    }

    private async Task<ApplicationUser?> FindCurrentUserAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _currentUser.UserId is { } userId
            ? await _users.FindByIdAsync(userId.ToString())
            : null;
    }

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

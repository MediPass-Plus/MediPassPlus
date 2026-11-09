using System.Security.Claims;
using MediPassPlus.Application.AdminManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>Admin-only account activation control (MP-29).</summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public class AdminUsersController : ControllerBase
{
    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string NotFoundType = "https://tools.ietf.org/html/rfc9110#section-15.5.5";

    private readonly IAccountStatusService _status;

    public AdminUsersController(IAccountStatusService status)
    {
        ArgumentNullException.ThrowIfNull(status);
        _status = status;
    }

    /// <summary>Deactivates an account. The user cannot log in again until it is reactivated.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, false, cancellationToken);

    /// <summary>Reactivates an account.</summary>
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> Reactivate(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, true, cancellationToken);

    private async Task<IActionResult> ChangeAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                type: UnauthorizedType,
                title: "Authentication required.",
                detail: "The access token does not identify a user.");
        }

        var failure = await _status.SetActiveAsync(id, isActive, adminId, cancellationToken);

        return failure switch
        {
            ChangeStatusFailure.None => NoContent(),
            ChangeStatusFailure.UserNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                type: NotFoundType,
                title: "User not found.",
                detail: "No account has the requested identifier."),
            ChangeStatusFailure.CannotChangeSelf => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                type: BadRequestType,
                title: "You cannot change your own status.",
                detail: "An administrator cannot deactivate or reactivate their own account."),
            _ => throw new InvalidOperationException($"Unexpected failure: {failure}.")
        };
    }
}
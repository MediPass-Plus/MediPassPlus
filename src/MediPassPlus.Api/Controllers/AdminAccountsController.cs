using MediPassPlus.Application.AdminManagement;
using MediPassPlus.Application.Dtos.AdminManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>Admin-only creation of Doctor and Staff accounts (MP-29).</summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminAccountsController : ControllerBase
{
    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
    private const string NotFoundType = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";

    private readonly IAccountAdminService _accounts;

    public AdminAccountsController(IAccountAdminService accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        _accounts = accounts;
    }

    /// <summary>Creates a doctor account with the Doctor role and a doctor profile.</summary>
    /// <response code="201">The doctor account was created and the Location header points at it.</response>
    /// <response code="400">The payload is invalid, or Identity rejected the account.</response>
    /// <response code="404">The clinic does not exist.</response>
    /// <response code="409">The email is already registered.</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="403">The caller is not an admin.</response>
    [HttpPost("doctors")]
    [ProducesResponseType(typeof(CreatedAccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateDoctor(CreateDoctorDto request, CancellationToken cancellationToken)
    {
        var result = await _accounts.CreateDoctorAsync(request, cancellationToken);
        return ToResponse(result, "/api/admin/doctors");
    }

    /// <summary>Creates a clinic staff account with the Staff role and a staff profile.</summary>
    /// <response code="201">The staff account was created and the Location header points at it.</response>
    /// <response code="400">The payload is invalid, or Identity rejected the account.</response>
    /// <response code="404">The clinic does not exist.</response>
    /// <response code="409">The email is already registered.</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="403">The caller is not an admin.</response>
    [HttpPost("staff")]
    [ProducesResponseType(typeof(CreatedAccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateStaff(CreateStaffDto request, CancellationToken cancellationToken)
    {
        var result = await _accounts.CreateStaffAsync(request, cancellationToken);
        return ToResponse(result, "/api/admin/staff");
    }

    private IActionResult ToResponse(CreateAccountResult result, string basePath)
    {
        if (result.IsSuccess)
        {
            return Created($"{basePath}/{result.Account!.UserId}", result.Account);
        }

        var detail = string.Join(" ", result.Errors);

        return result.Failure switch
        {
            CreateAccountFailure.ClinicNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                type: NotFoundType,
                title: "Clinic not found.",
                detail: detail),
            CreateAccountFailure.EmailAlreadyUsed => Problem(
                statusCode: StatusCodes.Status409Conflict,
                type: ConflictType,
                title: "Email already registered.",
                detail: detail),
            _ => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                type: BadRequestType,
                title: "The account could not be created.",
                detail: detail)
        };
    }
}
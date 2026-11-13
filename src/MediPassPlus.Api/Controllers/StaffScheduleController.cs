using System.Security.Claims;
using MediPassPlus.Application.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>Clinic staff schedule. Staff see reception fields only, never medical data (MP-40).</summary>
[ApiController]
[Route("api/staff/schedule")]
[Authorize(Roles = "Staff")]
public class StaffScheduleController : ControllerBase
{
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string ForbiddenType = "https://tools.ietf.org/html/rfc9110#section-15.5.4";

    private readonly IStaffScheduleService _schedule;

    public StaffScheduleController(IStaffScheduleService schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        _schedule = schedule;
    }

    /// <summary>Returns the appointments of one day for the staff member's own clinic.</summary>
    /// <param name="date">The day, for example 2026-11-10.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">The day's appointments with patient name, phone, time and status only.</response>
    /// <response code="401">The access token does not identify a user.</response>
    /// <response code="403">The caller has no clinic staff profile.</response>
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<StaffScheduleItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get([FromQuery] DateOnly date, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var staffUserId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                type: UnauthorizedType,
                title: "Authentication required.",
                detail: "The access token does not identify a user.");
        }

        var result = await _schedule.GetScheduleAsync(staffUserId, date, cancellationToken);

        if (!result.IsSuccess)
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                type: ForbiddenType,
                title: "No clinic staff profile.",
                detail: "This account is not linked to a clinic.");
        }

        return Ok(result.Items);
    }
}
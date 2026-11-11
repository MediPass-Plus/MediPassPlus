using System.Security.Claims;
using MediPassPlus.Application.DoctorSchedule;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>
/// The authenticated doctor's own daily schedule. The doctor is resolved from the access token, never from a
/// caller-supplied identifier, and the requested date is a clinic-local date in Africa/Cairo (FR-BKG-07, US-10).
/// </summary>
[ApiController]
[Route("api/doctors")]
public class DoctorScheduleController : ControllerBase
{
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string ForbiddenType = "https://tools.ietf.org/html/rfc9110#section-15.5.4";

    private readonly IDoctorScheduleService _doctorScheduleService;

    /// <summary>Initializes a new instance of the <see cref="DoctorScheduleController"/> class.</summary>
    /// <param name="doctorScheduleService">The doctor schedule application service.</param>
    public DoctorScheduleController(IDoctorScheduleService doctorScheduleService)
    {
        ArgumentNullException.ThrowIfNull(doctorScheduleService);
        _doctorScheduleService = doctorScheduleService;
    }

    /// <summary>
    /// Returns the authenticated doctor's appointments for one clinic-local day, ordered by slot start. Cancelled
    /// appointments are omitted, and only the patient's name is exposed (FR-BKG-07, FR-STF-04).
    /// </summary>
    /// <remarks>
    /// This list is deliberately not paged. NFR-PRF-04 asks for paging on lists, but a single clinic-local day for
    /// one doctor is a naturally bounded set and the doctor needs the whole day in one view; paging would hide
    /// appointments from the schedule.
    /// </remarks>
    /// <param name="date">The clinic-local date in Africa/Cairo.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ordered schedule for that day.</returns>
    /// <response code="200">The schedule was returned.</response>
    /// <response code="400">The date query parameter is missing or not a valid date.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The caller is not a doctor or has no doctor profile.</response>
    [Authorize(Roles = "Doctor")]
    [HttpGet("me/schedule")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<DoctorScheduleItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSchedule(
        [FromQuery] DateOnly date,
        CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var doctorUserId) || doctorUserId == Guid.Empty)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                type: UnauthorizedType,
                title: "Authentication required.",
                detail: "The access token does not identify a user.");
        }

        var result = await _doctorScheduleService.GetAsync(
            doctorUserId,
            date,
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Items);
        }

        return result.Failure switch
        {
            DoctorScheduleFailure.DoctorNotFound => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                type: ForbiddenType,
                title: "You cannot read this schedule.",
                detail: "The authenticated user has no doctor profile."),
            _ => throw new InvalidOperationException($"Unexpected schedule failure: {result.Failure}.")
        };
    }
}

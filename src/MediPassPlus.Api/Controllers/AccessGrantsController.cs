using System.Security.Claims;
using MediPassPlus.Api.Extensions;
using MediPassPlus.Application.VisitAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>
/// Patient control over a live visit-access grant. Ending the grant takes effect immediately, so the doctor's next
/// request is refused by the access-grant gate (FR-ACS-09, UC-19, NFR-PRI-02).
/// </summary>
[ApiController]
[Route("api/access-grants")]
[Authorize(Roles = "Patient")]
public class AccessGrantsController : ControllerBase
{
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string ForbiddenType = "https://tools.ietf.org/html/rfc9110#section-15.5.4";
    private const string NotFoundType = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";

    private readonly IAccessGrantEndService _endService;

    /// <summary>Initializes a new instance of the <see cref="AccessGrantsController"/> class.</summary>
    /// <param name="endService">The patient's immediate-end application service.</param>
    public AccessGrantsController(IAccessGrantEndService endService)
    {
        ArgumentNullException.ThrowIfNull(endService);
        _endService = endService;
    }

    /// <summary>
    /// Ends the authenticated patient's live access grant immediately. The end and its <c>AccessEnded</c> audit row
    /// commit together, and the doctor's next request is refused (FR-ACS-09, FR-ACS-11, ADR-0011 decision 4).
    /// </summary>
    /// <param name="grantId">The grant to end.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ended grant and its stored end.</returns>
    /// <response code="200">The grant ended immediately with reason <c>PatientEnded</c>.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The caller is not the patient who owns the grant's appointment.</response>
    /// <response code="404">No access grant has the requested identifier.</response>
    /// <response code="409">The grant already ended or reached the two-hour limit and cannot change state.</response>
    [HttpPost("{grantId:guid}/end")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(AccessGrantEndedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> End(Guid grantId, CancellationToken cancellationToken)
    {
        if (!TryGetPatientUserId(out var patientUserId))
        {
            return CreateProblem(
                StatusCodes.Status401Unauthorized,
                UnauthorizedType,
                "Authentication required.",
                "The access token does not identify a user.");
        }

        var result = await _endService.EndAsync(
            grantId,
            patientUserId,
            HttpContext.GetAuditIpAddress(),
            cancellationToken);

        if (result.IsSuccess)
        {
            // Grant details are personal access-control data and must not be cached by a browser or a proxy.
            Response.Headers["Cache-Control"] = "no-store";
            return Ok(result.Grant);
        }

        return result.Failure switch
        {
            AccessGrantEndFailure.GrantNotFound => CreateProblem(
                StatusCodes.Status404NotFound,
                NotFoundType,
                "Access grant not found.",
                "No access grant has the requested identifier."),
            AccessGrantEndFailure.PatientMismatch => CreateProblem(
                StatusCodes.Status403Forbidden,
                ForbiddenType,
                "You cannot end this access grant.",
                "You can end only an access grant for your own appointment."),
            AccessGrantEndFailure.GrantNotActive => CreateProblem(
                StatusCodes.Status409Conflict,
                ConflictType,
                "The access grant is not active.",
                "The access grant already ended or reached the two-hour limit."),
            _ => throw new InvalidOperationException(
                $"Unexpected access-grant end failure: {result.Failure}.")
        };
    }

    private bool TryGetPatientUserId(out Guid patientUserId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out patientUserId) && patientUserId != Guid.Empty;
    }

    private ObjectResult CreateProblem(int statusCode, string type, string title, string detail) =>
        Problem(statusCode: statusCode, type: type, title: title, detail: detail);
}

using System.Security.Claims;
using MediPassPlus.Api.Extensions;
using MediPassPlus.Application.VisitAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>
/// Patient decisions on a visit-access request. V2 answers a known request by its identifier; the scan-token path is
/// a later ticket. Approval creates the grant that the access-grant checker requires; rejection creates none.
/// </summary>
[ApiController]
[Route("api/access-requests")]
[Authorize(Roles = "Patient")]
public class AccessRequestsController : ControllerBase
{
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string ForbiddenType = "https://tools.ietf.org/html/rfc9110#section-15.5.4";
    private const string NotFoundType = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";

    private readonly IAccessRequestDecisionService _decisionService;

    /// <summary>Initializes a new instance of the <see cref="AccessRequestsController"/> class.</summary>
    /// <param name="decisionService">The approve/reject application service.</param>
    public AccessRequestsController(IAccessRequestDecisionService decisionService)
    {
        ArgumentNullException.ThrowIfNull(decisionService);
        _decisionService = decisionService;
    }

    /// <summary>
    /// Approves a pending request owned by the authenticated patient and creates the visit's access grant.
    /// </summary>
    /// <param name="requestId">The access request being approved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created grant.</returns>
    /// <response code="200">The request was approved and the grant was created.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The caller is not the patient who owns the request's appointment.</response>
    /// <response code="404">No access request has the requested identifier.</response>
    /// <response code="409">The request was already answered, or another decision won the race.</response>
    [HttpPost("{requestId:guid}/approve")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(AccessGrantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid requestId, CancellationToken cancellationToken)
    {
        if (!TryGetPatientUserId(out var patientUserId))
        {
            return CreateProblem(
                StatusCodes.Status401Unauthorized,
                UnauthorizedType,
                "Authentication required.",
                "The access token does not identify a user.");
        }

        var result = await _decisionService.ApproveAsync(
            requestId,
            patientUserId,
            HttpContext.GetAuditIpAddress(),
            cancellationToken);

        if (result.IsSuccess)
        {
            if (result.Grant is null)
            {
                throw new InvalidOperationException("A successful approval must contain a grant.");
            }

            // Grant details are personal access-control data and must not be cached by a browser or a proxy.
            Response.Headers["Cache-Control"] = "no-store";
            return Ok(result.Grant);
        }

        return MapFailure(result.Failure);
    }

    /// <summary>Rejects a pending request owned by the authenticated patient. No grant is created.</summary>
    /// <param name="requestId">The access request being rejected.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content when the request was rejected.</returns>
    /// <response code="204">The request was rejected and no grant was created.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The caller is not the patient who owns the request's appointment.</response>
    /// <response code="404">No access request has the requested identifier.</response>
    /// <response code="409">The request was already answered, or another decision won the race.</response>
    [HttpPost("{requestId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(Guid requestId, CancellationToken cancellationToken)
    {
        if (!TryGetPatientUserId(out var patientUserId))
        {
            return CreateProblem(
                StatusCodes.Status401Unauthorized,
                UnauthorizedType,
                "Authentication required.",
                "The access token does not identify a user.");
        }

        var result = await _decisionService.RejectAsync(
            requestId,
            patientUserId,
            HttpContext.GetAuditIpAddress(),
            cancellationToken);

        return result.IsSuccess ? NoContent() : MapFailure(result.Failure);
    }

    private bool TryGetPatientUserId(out Guid patientUserId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out patientUserId) && patientUserId != Guid.Empty;
    }

    private IActionResult MapFailure(AccessRequestDecisionFailure failure) =>
        failure switch
        {
            AccessRequestDecisionFailure.RequestNotFound => CreateProblem(
                StatusCodes.Status404NotFound,
                NotFoundType,
                "Access request not found.",
                "No access request has the requested identifier."),
            AccessRequestDecisionFailure.PatientMismatch => CreateProblem(
                StatusCodes.Status403Forbidden,
                ForbiddenType,
                "You cannot answer this access request.",
                "You can answer only an access request for your own appointment."),
            AccessRequestDecisionFailure.AlreadyAnswered => CreateProblem(
                StatusCodes.Status409Conflict,
                ConflictType,
                "The access request was already answered.",
                "A request can be approved or rejected only once."),
            AccessRequestDecisionFailure.RequestConflict => CreateProblem(
                StatusCodes.Status409Conflict,
                ConflictType,
                "The access request conflicted with another decision.",
                "Another decision answered this access request first."),
            _ => throw new InvalidOperationException($"Unexpected access-request decision failure: {failure}.")
        };

    private ObjectResult CreateProblem(int statusCode, string type, string title, string detail) =>
        Problem(statusCode: statusCode, type: type, title: title, detail: detail);
}

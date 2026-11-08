using System.Security.Claims;
using MediPassPlus.Application.VisitAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>
/// Visit-access endpoints. MP-4 provides the doctor's start-visit step; approval, record reads and visit completion
/// are later tickets.
/// </summary>
[ApiController]
[Route("api/appointments")]
public class VisitAccessController : ControllerBase
{
    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string ForbiddenType = "https://tools.ietf.org/html/rfc9110#section-15.5.4";
    private const string NotFoundType = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";

    private readonly IStartVisitService _startVisitService;

    /// <summary>Initializes a new instance of the <see cref="VisitAccessController"/> class.</summary>
    /// <param name="startVisitService">The start-visit application service.</param>
    public VisitAccessController(IStartVisitService startVisitService)
    {
        ArgumentNullException.ThrowIfNull(startVisitService);
        _startVisitService = startVisitService;
    }

    /// <summary>
    /// Starts a doctor's own appointment when it is today and checked in, and returns a fresh one-time QR token.
    /// The newest pending request is reused; when none is pending, a new request is created. Creating a request also
    /// stages an <c>AccessRequestCreated</c> audit row (ADR-0011 decision 8).
    /// </summary>
    /// <param name="appointmentId">The appointment to start.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The one-time QR token and its expiry.</returns>
    /// <response code="200">The visit was started and the QR token was issued.</response>
    /// <response code="400">The appointment is not today or is not checked in.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The caller is not a doctor, has no doctor profile, or does not own the appointment.</response>
    /// <response code="404">No appointment has the requested identifier.</response>
    /// <response code="409">
    /// A concurrent call created the appointment's pending request first. Retrying reuses that request.
    /// </response>
    [Authorize(Roles = "Doctor")]
    [HttpPost("{appointmentId:int}/start-visit")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(StartVisitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartVisit(
        int appointmentId,
        CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var doctorUserId))
        {
            return CreateProblem(
                StatusCodes.Status401Unauthorized,
                UnauthorizedType,
                "Authentication required.",
                "The access token does not identify a user.");
        }

        var result = await _startVisitService.StartAsync(
            appointmentId,
            doctorUserId,
            GetAuditIpAddress(),
            cancellationToken);

        if (result.IsSuccess)
        {
            // The QR token is a secret and must not be cached by the browser or a proxy.
            Response.Headers["Cache-Control"] = "no-store";
            return Ok(result.Response);
        }

        return result.Failure switch
        {
            StartVisitFailure.AppointmentNotFound => CreateProblem(
                StatusCodes.Status404NotFound,
                NotFoundType,
                "Appointment not found.",
                "No appointment has the requested identifier."),
            StartVisitFailure.DoctorNotFound or StartVisitFailure.DoctorMismatch => CreateProblem(
                StatusCodes.Status403Forbidden,
                ForbiddenType,
                "You cannot start this visit.",
                "You can start a visit only for your own appointment."),
            StartVisitFailure.AppointmentNotToday => CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The visit cannot be started yet.",
                "Only an appointment scheduled for today can be started."),
            StartVisitFailure.AppointmentNotCheckedIn => CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The visit cannot be started.",
                "The appointment must be checked in."),
            StartVisitFailure.RequestConflict => CreateProblem(
                StatusCodes.Status409Conflict,
                ConflictType,
                "The visit start conflicted with another request.",
                "Another request for this visit created its pending request first. Retry to reuse it."),
            _ => throw new InvalidOperationException($"Unexpected start-visit failure: {result.Failure}.")
        };
    }

    private string GetAuditIpAddress()
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        // AuditLog stores at most 45 characters; keep the event even when a scoped IPv6 address is longer.
        return string.IsNullOrWhiteSpace(ipAddress) || ipAddress.Length > 45
            ? "unknown"
            : ipAddress;
    }

    private ObjectResult CreateProblem(int statusCode, string type, string title, string detail) =>
        Problem(statusCode: statusCode, type: type, title: title, detail: detail);
}

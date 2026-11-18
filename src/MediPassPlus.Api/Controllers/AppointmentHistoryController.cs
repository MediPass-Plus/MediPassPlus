using MediPassPlus.Api.Authorization;
using MediPassPlus.Api.Extensions;
using MediPassPlus.Application.Identity;
using MediPassPlus.Application.Records;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>
/// The doctor read path for a patient's permanent record during a visit (FR-REC-02, FR-REC-07, ADR-0005). The
/// appointment is taken from the route, the grant is re-checked against the database by
/// <see cref="RequireActiveGrantAttribute"/>, and the patient is taken from the verified grant, so a doctor can never
/// read a record through a caller-supplied patient identifier (FR-ACS-10, FR-PRV-01).
/// </summary>
[ApiController]
[Route("api/appointments")]
[Authorize(Roles = "Doctor")]
public class AppointmentHistoryController : ControllerBase
{
    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";
    private const string ForbiddenType = "https://tools.ietf.org/html/rfc9110#section-15.5.4";
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";

    private readonly IRecordHistoryService _historyService;
    private readonly ILogger<AppointmentHistoryController> _logger;
    private readonly ICurrentUser _currentUser;

    /// <summary>Initializes a new instance of the <see cref="AppointmentHistoryController"/> class.</summary>
    /// <param name="historyService">The record history application service.</param>
    /// <param name="logger">Records wiring mistakes with identifiers only.</param>
    /// <param name="currentUser">The authenticated caller of this request.</param>
    public AppointmentHistoryController(
        IRecordHistoryService historyService,
        ILogger<AppointmentHistoryController> logger,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(historyService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(currentUser);

        _historyService = historyService;
        _logger = logger;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Returns one page of the permanent record of the patient the active grant points at: the record header, the
    /// diagnoses newest first with their prescriptions, and the report metadata. Diagnosis and prescription text is
    /// decrypted only after the grant check, and the <c>RecordRead</c> audit row is committed before the response is
    /// returned (FR-REC-02, FR-PRV-01, FR-PRV-02, NFR-PRF-04, ADR-0004, ADR-0005).
    /// </summary>
    /// <param name="appointmentId">The appointment the verified access grant is scoped to.</param>
    /// <param name="query">The page and page size of each list. The defaults are 1 and 20; the maximum is 50.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested page of the patient's record.</returns>
    /// <response code="200">The requested page, with clinical text decrypted and the read audited.</response>
    /// <response code="400">The page or page size is not valid.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">
    /// The caller is not a doctor, does not own the appointment, or has no active grant (no grant, an expired grant,
    /// a grant the patient ended, or another doctor's visit).
    /// </response>
    /// <response code="409">The patient's permanent record is missing, which breaks the registration invariant.</response>
    [HttpGet("{appointmentId:int}/history")]
    [RequireActiveGrant]
    [Produces("application/json")]
    [ProducesResponseType(typeof(RecordHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Get(
        int appointmentId,
        [FromQuery] RecordHistoryQuery query,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var doctorUserId))
        {
            return CreateProblem(
                StatusCodes.Status401Unauthorized,
                UnauthorizedType,
                "Authentication required.",
                "The access token does not identify a user.");
        }

        // The grant filter always stores the grant it verified. A missing value means the filter did not run, so the
        // request fails closed instead of falling back to a caller-supplied scope.
        var grant = HttpContext.GetVerifiedAccessGrant();
        if (grant is null)
        {
            _logger.LogError(
                "The appointment history action ran without a verified access grant for appointment {AppointmentId}.",
                appointmentId);

            return CreateProblem(
                StatusCodes.Status403Forbidden,
                ForbiddenType,
                "You cannot access this record.",
                "An active access grant is required for this appointment.");
        }

        var result = await _historyService.GetForDoctorAsync(
            appointmentId,
            grant,
            doctorUserId,
            query,
            HttpContext.GetAuditIpAddress(),
            cancellationToken);

        if (result.IsSuccess)
        {
            // Medical responses are personal data and must not be cached by a browser or a proxy.
            Response.Headers["Cache-Control"] = "no-store";
            return Ok(result.History);
        }

        return result.Failure switch
        {
            RecordHistoryFailure.ScopeMismatch => CreateProblem(
                StatusCodes.Status403Forbidden,
                ForbiddenType,
                "You cannot access this record.",
                "An active access grant is required for this appointment."),
            RecordHistoryFailure.RecordNotFound => CreateProblem(
                StatusCodes.Status409Conflict,
                ConflictType,
                "The patient record is not available.",
                "The patient's permanent record could not be found."),
            _ => throw new InvalidOperationException(
                $"Unexpected record history failure: {result.Failure}.")
        };
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = _currentUser.UserId.GetValueOrDefault();

        return userId != Guid.Empty;
    }

    private ObjectResult CreateProblem(int statusCode, string type, string title, string detail) =>
        Problem(statusCode: statusCode, type: type, title: title, detail: detail);
}

using MediPassPlus.Application.Audit;
using MediPassPlus.Application.Common;
using MediPassPlus.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>
/// The patient-facing "who viewed my record" surface: the authenticated patient reads the audit rows of their own
/// record, newest first and paged (FR-PRV-03, US-32, UC-20, ADR-0004). The patient is always resolved from the token,
/// so the route takes no patient identifier and one patient can never reach another patient's audit history. The list
/// itself is not audited (ADR-0004 decision 4).
/// </summary>
[ApiController]
[Route("api/me/audit")]
[Authorize(Roles = "Patient")]
public class MyAuditController : ControllerBase
{
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";

    private readonly IAuditLogQueryService _auditLogQueryService;
    private readonly ICurrentUser _currentUser;

    /// <summary>Initializes a new instance of the <see cref="MyAuditController"/> class.</summary>
    /// <param name="auditLogQueryService">The audit list application service.</param>
    /// <param name="currentUser">The authenticated caller of this request.</param>
    public MyAuditController(IAuditLogQueryService auditLogQueryService, ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(auditLogQueryService);
        ArgumentNullException.ThrowIfNull(currentUser);

        _auditLogQueryService = auditLogQueryService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Returns one page of the authenticated patient's own audit rows, newest first. Each entry carries the actor,
    /// action, entity type, entity identifier, time and IP address only, never medical content (FR-PRV-02,
    /// FR-PRV-03, NFR-PRF-04, ADR-0004).
    /// </summary>
    /// <param name="query">The page and page size. The defaults are 1 and 20; the maximum is 50.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested page of the authenticated patient's audit rows.</returns>
    /// <response code="200">The requested page, newest first.</response>
    /// <response code="400">The page or page size is not valid.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The caller is not a patient.</response>
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(typeof(PagedResult<AuditLogEntryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(
        [FromQuery] MyAuditQuery query,
        CancellationToken cancellationToken)
    {
        var patientUserId = _currentUser.UserId.GetValueOrDefault();

        if (patientUserId == Guid.Empty)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                type: UnauthorizedType,
                title: "Authentication required.",
                detail: "The access token does not identify a user.");
        }

        var page = await _auditLogQueryService.GetForPatientAsync(
            patientUserId,
            query,
            cancellationToken);

        // Audit history is personal data and must not be cached by a browser or a proxy.
        Response.Headers["Cache-Control"] = "no-store";

        return Ok(page);
    }
}

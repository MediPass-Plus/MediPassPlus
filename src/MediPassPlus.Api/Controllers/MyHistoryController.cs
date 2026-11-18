using MediPassPlus.Api.Extensions;
using MediPassPlus.Application.Identity;
using MediPassPlus.Application.Records;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>
/// The patient-facing history surface: the authenticated patient reads their own permanent record at any time,
/// without an access grant (FR-REC-07, ADR-0005). The patient is always resolved from the token, so the route takes
/// no patient identifier and one patient can never reach another patient's record.
/// </summary>
[ApiController]
[Route("api/me/history")]
[Authorize(Roles = "Patient")]
public class MyHistoryController : ControllerBase
{
    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";

    private readonly IRecordHistoryService _historyService;
    private readonly ICurrentUser _currentUser;

    /// <summary>Initializes a new instance of the <see cref="MyHistoryController"/> class.</summary>
    /// <param name="historyService">The record history application service.</param>
    /// <param name="currentUser">The authenticated caller of this request.</param>
    public MyHistoryController(IRecordHistoryService historyService, ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(historyService);
        ArgumentNullException.ThrowIfNull(currentUser);

        _historyService = historyService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Returns one page of the authenticated patient's permanent record: the record header, the diagnoses newest
    /// first with their prescriptions, and the report metadata. No access grant is required, and the
    /// <c>RecordRead</c> audit row records the patient as both the actor and the owner (FR-REC-07, FR-PRV-02,
    /// NFR-PRF-04, ADR-0004, ADR-0005).
    /// </summary>
    /// <param name="query">The page and page size of each list. The defaults are 1 and 20; the maximum is 50.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested page of the authenticated patient's record.</returns>
    /// <response code="200">The requested page, with clinical text decrypted and the self-read audited.</response>
    /// <response code="400">The page or page size is not valid.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The caller is not a patient.</response>
    /// <response code="409">The patient's permanent record is missing, which breaks the registration invariant.</response>
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(typeof(RecordHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Get(
        [FromQuery] RecordHistoryQuery query,
        CancellationToken cancellationToken)
    {
        if (!TryGetPatientUserId(out var patientUserId))
        {
            return CreateProblem(
                StatusCodes.Status401Unauthorized,
                UnauthorizedType,
                "Authentication required.",
                "The access token does not identify a user.");
        }

        var result = await _historyService.GetForPatientAsync(
            patientUserId,
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
            RecordHistoryFailure.RecordNotFound => CreateProblem(
                StatusCodes.Status409Conflict,
                ConflictType,
                "The patient record is not available.",
                "Your permanent record could not be found."),
            _ => throw new InvalidOperationException(
                $"Unexpected record history failure: {result.Failure}.")
        };
    }

    private bool TryGetPatientUserId(out Guid patientUserId)
    {
        patientUserId = _currentUser.UserId.GetValueOrDefault();

        return patientUserId != Guid.Empty;
    }

    private ObjectResult CreateProblem(int statusCode, string type, string title, string detail) =>
        Problem(statusCode: statusCode, type: type, title: title, detail: detail);
}

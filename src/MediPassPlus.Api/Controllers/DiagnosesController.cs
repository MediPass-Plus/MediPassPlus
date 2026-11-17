using System.Security.Claims;
using MediPassPlus.Api.Authorization;
using MediPassPlus.Api.Extensions;
using MediPassPlus.Application.Records;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>
/// The doctor write path for recording a diagnosis during a visit (FR-REC-03, ADR-0005). The appointment, patient
/// and medical record are derived from the verified access grant on the server; the request body carries only the
/// diagnosis text, so a caller can never choose another patient's record.
/// </summary>
[ApiController]
[Route("api/appointments")]
[Authorize(Roles = "Doctor")]
public class DiagnosesController : ControllerBase
{
    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string ForbiddenType = "https://tools.ietf.org/html/rfc9110#section-15.5.4";
    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";

    private readonly IDiagnosisWriteService _diagnosisWriteService;

    /// <summary>Initializes a new instance of the <see cref="DiagnosesController"/> class.</summary>
    /// <param name="diagnosisWriteService">The diagnosis write application service.</param>
    public DiagnosesController(IDiagnosisWriteService diagnosisWriteService)
    {
        ArgumentNullException.ThrowIfNull(diagnosisWriteService);
        _diagnosisWriteService = diagnosisWriteService;
    }

    /// <summary>
    /// Records a diagnosis for the doctor's own appointment while the visit is in progress, or during the
    /// post-completion grace window while the access grant is still active (FR-REC-03, FR-ACS-07, ADR-0005). The
    /// description is encrypted before it is stored (NFR-SEC-05) and the <c>DiagnosisCreated</c> audit row commits in
    /// the same unit of work (ADR-0004 decision 7).
    /// </summary>
    /// <param name="appointmentId">The appointment the verified access grant is scoped to.</param>
    /// <param name="request">The plaintext diagnosis text.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new diagnosis identifier and its server timestamp.</returns>
    /// <response code="201">The diagnosis was recorded and its audit row was committed.</response>
    /// <response code="400">The description is empty or longer than 4,000 characters, or the visit is not open.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The caller is not a doctor, does not own the appointment, or has no active grant.</response>
    /// <response code="409">The patient's permanent record is missing, which breaks the registration invariant.</response>
    [HttpPost("{appointmentId:int}/diagnoses")]
    [RequireActiveGrant]
    [Produces("application/json")]
    [ProducesResponseType(typeof(DiagnosisResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        int appointmentId,
        CreateDiagnosisRequest request,
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

        var result = await _diagnosisWriteService.CreateDiagnosisAsync(
            appointmentId,
            doctorUserId,
            request,
            HttpContext.GetAuditIpAddress(),
            cancellationToken);

        if (result.IsSuccess)
        {
            // Medical responses are personal data and must not be cached by a browser or a proxy.
            Response.Headers["Cache-Control"] = "no-store";
            return StatusCode(StatusCodes.Status201Created, result.Diagnosis);
        }

        return result.Failure switch
        {
            DiagnosisWriteFailure.GrantNotActive or
            DiagnosisWriteFailure.AppointmentNotFound or
            DiagnosisWriteFailure.ScopeMismatch => CreateProblem(
                StatusCodes.Status403Forbidden,
                ForbiddenType,
                "You cannot access this record.",
                "An active access grant is required for this appointment."),
            DiagnosisWriteFailure.AppointmentNotInVisit => CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The visit is not open for record writes.",
                "A diagnosis can be recorded only during the visit or while the post-completion grant is active."),
            DiagnosisWriteFailure.MedicalRecordNotFound => CreateProblem(
                StatusCodes.Status409Conflict,
                ConflictType,
                "The patient record is not available.",
                "The patient's permanent record could not be found."),
            _ => throw new InvalidOperationException(
                $"Unexpected diagnosis write failure: {result.Failure}.")
        };
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out userId) && userId != Guid.Empty;
    }

    private ObjectResult CreateProblem(int statusCode, string type, string title, string detail) =>
        Problem(statusCode: statusCode, type: type, title: title, detail: detail);
}

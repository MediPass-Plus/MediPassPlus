using MediPassPlus.Api.Authorization;
using MediPassPlus.Api.Extensions;
using MediPassPlus.Application.Identity;
using MediPassPlus.Application.Records;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>
/// The doctor write path for adding a prescription to an existing diagnosis (FR-REC-04, ADR-0005). The diagnosis is
/// resolved on the server to the appointment and patient that own it, so the grant is scoped to the stored row and
/// never to a caller-supplied appointment or patient identifier.
/// </summary>
[ApiController]
[Route("api/diagnoses")]
[Authorize(Roles = "Doctor")]
public class PrescriptionsController : ControllerBase
{
    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string ForbiddenType = "https://tools.ietf.org/html/rfc9110#section-15.5.4";
    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";

    private readonly IDiagnosisWriteService _diagnosisWriteService;
    private readonly ICurrentUser _currentUser;

    /// <summary>Initializes a new instance of the <see cref="PrescriptionsController"/> class.</summary>
    /// <param name="diagnosisWriteService">The diagnosis write application service.</param>
    /// <param name="currentUser">The authenticated caller of this request.</param>
    public PrescriptionsController(IDiagnosisWriteService diagnosisWriteService, ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(diagnosisWriteService);
        ArgumentNullException.ThrowIfNull(currentUser);

        _diagnosisWriteService = diagnosisWriteService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Adds one prescription to a diagnosis of the doctor's own visit while the visit is in progress, or during the
    /// post-completion grace window while the access grant is still active (FR-REC-04, FR-ACS-07, ADR-0005). Every
    /// text field is encrypted before it is stored (NFR-SEC-05), the row is append-only (FR-REC-09), and the
    /// <c>PrescriptionCreated</c> audit row commits in the same unit of work (ADR-0004 decision 7).
    /// </summary>
    /// <param name="diagnosisId">The diagnosis the prescription belongs to.</param>
    /// <param name="request">The plaintext medicine name, dosage and optional notes.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The new prescription identifier and its diagnosis.</returns>
    /// <response code="201">The prescription was added and its audit row was committed.</response>
    /// <response code="400">A required field is empty, a limit is exceeded, or the visit is not open.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">
    /// The caller is not a doctor, does not own the visit, has no active grant, or the diagnosis is outside the
    /// grant's appointment and patient scope.
    /// </response>
    /// <response code="409">The patient's permanent record is missing, which breaks the registration invariant.</response>
    [HttpPost("{diagnosisId:guid}/prescriptions")]
    [RequireActiveGrantForDiagnosis]
    [Produces("application/json")]
    [ProducesResponseType(typeof(PrescriptionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        Guid diagnosisId,
        CreatePrescriptionRequest request,
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

        var result = await _diagnosisWriteService.CreatePrescriptionAsync(
            diagnosisId,
            doctorUserId,
            request,
            HttpContext.GetAuditIpAddress(),
            cancellationToken);

        if (result.IsSuccess)
        {
            // Medical responses are personal data and must not be cached by a browser or a proxy.
            Response.Headers["Cache-Control"] = "no-store";
            return StatusCode(StatusCodes.Status201Created, result.Prescription);
        }

        return result.Failure switch
        {
            DiagnosisWriteFailure.GrantNotActive or
            DiagnosisWriteFailure.AppointmentNotFound or
            DiagnosisWriteFailure.ScopeMismatch => CreateProblem(
                StatusCodes.Status403Forbidden,
                ForbiddenType,
                "You cannot access this record.",
                "An active access grant is required for this diagnosis."),
            DiagnosisWriteFailure.AppointmentNotInVisit => CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The visit is not open for record writes.",
                "A prescription can be added only during the visit or while the post-completion grant is active."),
            DiagnosisWriteFailure.MedicalRecordNotFound => CreateProblem(
                StatusCodes.Status409Conflict,
                ConflictType,
                "The patient record is not available.",
                "The patient's permanent record could not be found."),
            _ => throw new InvalidOperationException(
                $"Unexpected prescription write failure: {result.Failure}.")
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

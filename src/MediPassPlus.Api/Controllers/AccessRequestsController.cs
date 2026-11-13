using System.Security.Claims;
using MediPassPlus.Api.Extensions;
using MediPassPlus.Api.RateLimiting;
using MediPassPlus.Application.VisitAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediPassPlus.Api.Controllers;

/// <summary>
/// Patient decisions on a visit-access request. A patient scans the doctor's QR token as text or as a camera image,
/// then approves or rejects the request it proves; a known request can also be answered by its identifier. Approval
/// creates the grant that the access-grant checker requires; rejection creates none. Every action shares the
/// per-patient <c>QrApprove</c> rate limit (ADR-0007 decision 2, FR-PRV-07, MP-97).
/// </summary>
[ApiController]
[Route("api/access-requests")]
[Authorize(Roles = "Patient")]
[EnableRateLimiting(QrApproveRateLimitPolicy.Name)]
public class AccessRequestsController : ControllerBase
{
    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string ForbiddenType = "https://tools.ietf.org/html/rfc9110#section-15.5.4";
    private const string NotFoundType = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
    private const string ConflictType = "https://tools.ietf.org/html/rfc9110#section-15.5.10";
    private const string GoneType = "https://tools.ietf.org/html/rfc9110#section-15.5.11";
    private const string UnsupportedMediaTypeType = "https://tools.ietf.org/html/rfc9110#section-15.5.16";

    private readonly IAccessRequestScanService _scanService;
    private readonly IAccessRequestDecisionService _decisionService;

    /// <summary>Initializes a new instance of the <see cref="AccessRequestsController"/> class.</summary>
    /// <param name="scanService">Resolves a presented QR token to the request it proves.</param>
    /// <param name="decisionService">The approve/reject application service.</param>
    public AccessRequestsController(
        IAccessRequestScanService scanService,
        IAccessRequestDecisionService decisionService)
    {
        ArgumentNullException.ThrowIfNull(scanService);
        ArgumentNullException.ThrowIfNull(decisionService);

        _scanService = scanService;
        _decisionService = decisionService;
    }

    /// <summary>
    /// Resolves a QR token presented as text and returns the doctor and clinic details the patient must see before
    /// deciding. Scanning never consumes the token; approving or rejecting it does (UC-12, FR-ACS-02).
    /// </summary>
    /// <param name="request">The plaintext QR token from the request body.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request details the patient is deciding on.</returns>
    /// <response code="200">The token is valid and the request details are returned.</response>
    /// <response code="400">The token is missing or longer than the accepted length.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The token is unknown or belongs to another patient's appointment.</response>
    /// <response code="410">
    /// The token is expired, consumed, revoked or locked, its request was already answered, or the visit is no longer
    /// in progress.
    /// </response>
    /// <response code="429">
    /// The patient exceeded the configured scan/decision limit; the response carries <c>Retry-After</c>.
    /// </response>
    [HttpPost("~/api/access/scan")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(AccessRequestScanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Scan(
        QrTokenRequest request,
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

        var result = await _scanService.ScanAsync(
            request.Token,
            patientUserId,
            cancellationToken);

        return ToScanResponse(result);
    }

    /// <summary>
    /// Decodes a QR code from an uploaded camera image in memory and resolves it exactly like the text scan. The
    /// image is never stored, and the decoded token is never echoed back (UC-12, MP-97).
    /// </summary>
    /// <param name="image">The camera frame, as a JPEG or PNG upload up to 5 MB.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request details the patient is deciding on.</returns>
    /// <response code="200">The image contained a valid token and the request details are returned.</response>
    /// <response code="400">The image is missing, larger than 5 MB, corrupt, or contains no readable QR code.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The decoded token is unknown or belongs to another patient's appointment.</response>
    /// <response code="410">
    /// The decoded token is expired, consumed, revoked or locked, its request was already answered, or the visit is
    /// no longer in progress.
    /// </response>
    /// <response code="415">The uploaded part is not an image.</response>
    /// <response code="429">
    /// The patient exceeded the configured scan/decision limit; the response carries <c>Retry-After</c>.
    /// </response>
    [HttpPost("scan-image")]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(AccessRequestScanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ScanImage(
        IFormFile? image,
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

        if (image is null || image.Length == 0)
        {
            return CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The image is not readable.",
                "Upload a camera image that contains the doctor's QR code.");
        }

        if (image.ContentType is null
            || !image.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return CreateProblem(
                StatusCodes.Status415UnsupportedMediaType,
                UnsupportedMediaTypeType,
                "The upload is not an image.",
                "Upload the camera frame as a JPEG or PNG image.");
        }

        if (image.Length > AccessRequestScanService.MaxImageBytes)
        {
            return CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The image is too large.",
                "The camera image must be at most 5 MB.");
        }

        using var buffer = new MemoryStream();
        await image.CopyToAsync(buffer, cancellationToken);

        var result = await _scanService.ScanImageAsync(
            buffer.ToArray(),
            patientUserId,
            cancellationToken);

        return ToScanResponse(result);
    }

    /// <summary>
    /// Approves the pending request proved by a scanned QR token and creates the visit's access grant. The token is
    /// resolved by its stored hash; the plaintext is never logged or stored (UC-12, FR-ACS-03, FR-ACS-04).
    /// </summary>
    /// <param name="request">The plaintext QR token from the request body.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created grant.</returns>
    /// <response code="200">The request was approved and the grant was created.</response>
    /// <response code="400">The token is missing or longer than the accepted length.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The token is unknown or belongs to another patient's appointment.</response>
    /// <response code="409">Another decision won the race.</response>
    /// <response code="410">
    /// The token is expired, consumed, revoked or locked, its request was already answered, or the visit is no longer
    /// in progress.
    /// </response>
    /// <response code="429">
    /// The patient exceeded the configured scan/decision limit; the response carries <c>Retry-After</c>.
    /// </response>
    [HttpPost("token/approve")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(AccessGrantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ApproveByToken(
        QrTokenRequest request,
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

        var result = await _decisionService.ApproveByTokenAsync(
            request.Token,
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

        return MapFailure(result.Failure, tokenPath: true);
    }

    /// <summary>
    /// Rejects the pending request proved by a scanned QR token. No grant is created, and the token and any fallback
    /// code are revoked in the same unit of work.
    /// </summary>
    /// <param name="request">The plaintext QR token from the request body.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content when the request was rejected.</returns>
    /// <response code="204">The request was rejected and no grant was created.</response>
    /// <response code="400">The token is missing or longer than the accepted length.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The token is unknown or belongs to another patient's appointment.</response>
    /// <response code="409">Another decision won the race.</response>
    /// <response code="410">
    /// The token is expired, consumed, revoked or locked, or its request was already answered.
    /// </response>
    /// <response code="429">
    /// The patient exceeded the configured scan/decision limit; the response carries <c>Retry-After</c>.
    /// </response>
    [HttpPost("token/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RejectByToken(
        QrTokenRequest request,
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

        var result = await _decisionService.RejectByTokenAsync(
            request.Token,
            patientUserId,
            HttpContext.GetAuditIpAddress(),
            cancellationToken);

        return result.IsSuccess ? NoContent() : MapFailure(result.Failure, tokenPath: true);
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
    /// <response code="409">
    /// The request was already answered, another decision won the race, or the visit is no longer in progress.
    /// </response>
    /// <response code="429">
    /// The patient exceeded the configured approve/reject limit; the response carries <c>Retry-After</c>.
    /// </response>
    [HttpPost("{requestId:guid}/approve")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(AccessGrantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
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

        return MapFailure(result.Failure, tokenPath: false);
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
    /// <response code="429">
    /// The patient exceeded the configured approve/reject limit; the response carries <c>Retry-After</c>.
    /// </response>
    [HttpPost("{requestId:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
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

        return result.IsSuccess ? NoContent() : MapFailure(result.Failure, tokenPath: false);
    }

    private IActionResult ToScanResponse(AccessRequestScanResult result)
    {
        if (!result.IsSuccess)
        {
            return result.Failure switch
            {
                AccessRequestScanFailure.TokenNotFound => CreateTokenNotFoundProblem(),
                AccessRequestScanFailure.TokenUnusable => CreateProblem(
                    StatusCodes.Status410Gone,
                    GoneType,
                    "The QR token is no longer valid.",
                    "The token expired, was already used or revoked, or its request was already answered."),
                AccessRequestScanFailure.VisitNotOpen => CreateProblem(
                    StatusCodes.Status410Gone,
                    GoneType,
                    "The visit is no longer in progress.",
                    "Access can be approved only while the appointment is checked in or in visit."),
                AccessRequestScanFailure.ImageNotReadable => CreateProblem(
                    StatusCodes.Status400BadRequest,
                    BadRequestType,
                    "The image is not readable.",
                    "Upload a camera image that contains the doctor's QR code."),
                _ => throw new InvalidOperationException(
                    $"Unexpected access-request scan failure: {result.Failure}.")
            };
        }

        if (result.Response is null)
        {
            throw new InvalidOperationException("A successful scan must contain a response.");
        }

        // Doctor, clinic and appointment details are personal access-control data; do not cache them.
        Response.Headers["Cache-Control"] = "no-store";
        return Ok(result.Response);
    }

    private bool TryGetPatientUserId(out Guid patientUserId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out patientUserId) && patientUserId != Guid.Empty;
    }

    private IActionResult MapFailure(AccessRequestDecisionFailure failure, bool tokenPath) =>
        failure switch
        {
            AccessRequestDecisionFailure.RequestNotFound when tokenPath => CreateTokenNotFoundProblem(),
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
            AccessRequestDecisionFailure.TokenNotFound => CreateTokenNotFoundProblem(),
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
            AccessRequestDecisionFailure.TokenUnusable => CreateProblem(
                StatusCodes.Status410Gone,
                GoneType,
                "The QR token is no longer valid.",
                "The token expired, was already used or revoked, or its request was already answered."),
            AccessRequestDecisionFailure.VisitNotOpen when tokenPath => CreateProblem(
                StatusCodes.Status410Gone,
                GoneType,
                "The visit is no longer in progress.",
                "Access can be approved only while the appointment is checked in or in visit."),
            AccessRequestDecisionFailure.VisitNotOpen => CreateProblem(
                StatusCodes.Status409Conflict,
                ConflictType,
                "The visit is no longer in progress.",
                "Access can be approved only while the appointment is checked in or in visit."),
            _ => throw new InvalidOperationException($"Unexpected access-request decision failure: {failure}.")
        };

    private ObjectResult CreateTokenNotFoundProblem() =>
        CreateProblem(
            StatusCodes.Status403Forbidden,
            ForbiddenType,
            "The QR token cannot be used.",
            "The token is unknown or belongs to another patient's appointment.");

    private ObjectResult CreateProblem(int statusCode, string type, string title, string detail) =>
        Problem(statusCode: statusCode, type: type, title: title, detail: detail);
}

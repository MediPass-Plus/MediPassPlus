using System.IO;
using MediPassPlus.Api.Extensions;
using MediPassPlus.Application.Identity;
using MediPassPlus.Application.Records;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

/// <summary>
/// The report upload endpoint (FR-REC-05, UC-16). A patient uploads to their own record and a doctor to a record
/// covered by an active grant; the record owner and the grant are resolved on the server, and a caller can never
/// supply a patient id. Every refusal for a record the caller may not use answers the same <c>404</c>, so the response
/// never confirms that another patient's record exists (NFR-PRI-01, ADR-0015).
/// </summary>
[ApiController]
[Route("api/records")]
[Authorize(Roles = "Doctor,Patient")]
public sealed class ReportsController : ControllerBase
{
    private const string BadRequestType = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string NotFoundType = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
    private const string PayloadTooLargeType = "https://tools.ietf.org/html/rfc9110#section-15.5.14";
    private const string UnsupportedMediaTypeType = "https://tools.ietf.org/html/rfc9110#section-15.5.16";

    // The form limit leaves room for the multipart headers and boundaries, so a file exactly at the 10 MB limit is
    // always accepted and the size rule is answered by the service instead of by the framework (NFR-SEC-09). A body
    // past this window is refused by the multipart reader with a 400 before the action runs; both answers are client
    // errors and neither stores a byte.
    private const long MaxRequestBytes = ReportFileValidator.MaxSizeBytes + (64 * 1024);

    private const string NotFoundTitle = "Not found.";
    private const string NotFoundDetail = "No record is available to this caller.";

    private readonly IReportUploadService _uploadService;
    private readonly ICurrentUser _currentUser;

    /// <summary>Initializes a new instance of the <see cref="ReportsController"/> class.</summary>
    /// <param name="uploadService">The upload application service.</param>
    /// <param name="currentUser">The authenticated caller of this request.</param>
    public ReportsController(IReportUploadService uploadService, ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(uploadService);
        ArgumentNullException.ThrowIfNull(currentUser);

        _uploadService = uploadService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Uploads one report or scan as PDF, JPEG or PNG up to 10 MB (FR-REC-05, FR-REC-06). The file is stored privately
    /// under a random name, the <c>ReportUploaded</c> audit row commits in the same unit of work, and the response
    /// carries metadata only: the stored file is never reachable by a link (FR-PRV-05, ADR-0015 decision 6).
    /// </summary>
    /// <param name="recordId">The permanent record the report belongs to.</param>
    /// <param name="file">The multipart part named <c>file</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored report's metadata.</returns>
    /// <response code="201">The file was stored and the report row with its audit entry was committed.</response>
    /// <response code="400">The request carries no <c>file</c> part, or the part is empty.</response>
    /// <response code="401">The access token is missing, invalid, expired, or does not identify a user.</response>
    /// <response code="403">The caller is not a doctor or a patient.</response>
    /// <response code="404">
    /// No record has that identifier, the caller may not use it, or a doctor holds no active grant for its owner. The
    /// three cases are deliberately indistinguishable.
    /// </response>
    /// <response code="413">The file is larger than 10 MB.</response>
    /// <response code="415">The media type is not PDF, JPEG or PNG, or the bytes do not match the declared type.</response>
    [HttpPost("{recordId:guid}/reports")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ReportMetadataResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> Upload(
        Guid recordId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var callerUserId))
        {
            return CreateProblem(
                StatusCodes.Status401Unauthorized,
                UnauthorizedType,
                "Authentication required.",
                "The access token does not identify a user.");
        }

        if (file is null)
        {
            return CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The request must carry a file part named 'file'.",
                "Send the report as multipart/form-data under the form field 'file'.");
        }

        // The role comes from the authenticated identity, never from the form or the query string (FR-ACS-06).
        var callerRole = User.IsInRole("Doctor")
            ? ReportUploaderRole.Doctor
            : ReportUploaderRole.Patient;

        var result = await _uploadService.UploadAsync(
            recordId,
            callerUserId,
            callerRole,
            new FormFileUpload(file),
            HttpContext.GetAuditIpAddress(),
            cancellationToken);

        if (result.IsSuccess)
        {
            // Medical metadata is personal data and must not be cached by a browser or a proxy.
            Response.Headers["Cache-Control"] = "no-store";
            return StatusCode(StatusCodes.Status201Created, result.Report);
        }

        return result.Failure switch
        {
            ReportUploadFailure.RecordNotFound or
            ReportUploadFailure.GrantNotActive => CreateProblem(
                StatusCodes.Status404NotFound,
                NotFoundType,
                NotFoundTitle,
                NotFoundDetail),
            ReportUploadFailure.EmptyFile => CreateProblem(
                StatusCodes.Status400BadRequest,
                BadRequestType,
                "The uploaded file is empty.",
                "Upload a PDF, JPEG or PNG file up to 10 MB."),
            ReportUploadFailure.TooLarge => CreateProblem(
                StatusCodes.Status413PayloadTooLarge,
                PayloadTooLargeType,
                "The uploaded file is too large.",
                "The maximum size for a report is 10 MB."),
            ReportUploadFailure.UnsupportedContentType or
            ReportUploadFailure.ContentMismatch => CreateProblem(
                StatusCodes.Status415UnsupportedMediaType,
                UnsupportedMediaTypeType,
                "Unsupported file type.",
                "Only PDF, JPEG and PNG files are accepted, and the file content must match its declared type."),
            _ => throw new InvalidOperationException($"Unexpected report upload failure: {result.Failure}.")
        };
    }

    private bool TryGetUserId(out Guid userId)
    {
        userId = _currentUser.UserId.GetValueOrDefault();

        return userId != Guid.Empty;
    }

    private ObjectResult CreateProblem(int statusCode, string type, string title, string detail) =>
        Problem(statusCode: statusCode, type: type, title: title, detail: detail);

    /// <summary>Adapts the request part to the application layer's upload contract.</summary>
    private sealed class FormFileUpload : IUploadedReportFile
    {
        private readonly IFormFile _file;

        public FormFileUpload(IFormFile file)
        {
            ArgumentNullException.ThrowIfNull(file);
            _file = file;
        }

        public string? ContentType => _file.ContentType;

        public long Length => _file.Length;

        // Safe to call more than once: the upload reads the leading bytes for the format check and then copies the
        // whole part, so it never buffers the file in memory to do both.
        public Stream OpenReadStream() => _file.OpenReadStream();
    }
}

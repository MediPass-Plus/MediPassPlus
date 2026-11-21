using MediPassPlus.Application.Records;

namespace MediPassPlus.Api.Records;

/// <summary>
/// The HTTP window for a report upload: the 10 MB file limit plus room for the multipart headers and boundaries. The
/// window is deliberately larger than the file rule so a file exactly at 10 MB is decided by the application service,
/// and a body past the window is refused with <c>413</c> before the form reader buffers it (FR-REC-05, NFR-SEC-09).
/// </summary>
public static class ReportUploadRequestLimits
{
    /// <summary>The largest accepted request body, including the multipart envelope.</summary>
    public const long MaxRequestBytes = ReportFileValidator.MaxSizeBytes + (64 * 1024);

    /// <summary>The RFC 9110 problem type for <c>413 Payload Too Large</c>.</summary>
    public const string PayloadTooLargeType = "https://tools.ietf.org/html/rfc9110#section-15.5.14";
}

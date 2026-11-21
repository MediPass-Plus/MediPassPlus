using System.IO;

namespace MediPassPlus.Application.Records;

/// <summary>
/// One uploaded file as the application layer sees it, without depending on ASP.NET Core's <c>IFormFile</c>. The
/// controller adapts the request part to this contract, so the upload rules live in the Application layer and stay
/// testable without HTTP (FR-REC-06, NFR-SEC-09).
/// </summary>
public interface IUploadedReportFile
{
    /// <summary>
    /// The media type the client declared for the part, or null when it declared none. It is a claim, and
    /// <see cref="ReportFileValidator"/> compares it with the bytes.
    /// </summary>
    string? ContentType { get; }

    /// <summary>The length the client reported for the part. It is a claim too: the storage counts the real bytes.</summary>
    long Length { get; }

    /// <summary>
    /// Opens the file content for reading. The caller may call it more than once, and every call must return a stream
    /// positioned at the first byte: the upload reads the leading bytes to check the format and then copies the whole
    /// file, without buffering it in memory.
    /// </summary>
    /// <returns>A readable stream over the file content.</returns>
    Stream OpenReadStream();
}

namespace MediPassPlus.Application.Records;

/// <summary>
/// Why an uploaded report file was refused. The upload endpoint maps each value to a status code; the file is never
/// stored when a failure is present (FR-REC-06, NFR-SEC-09).
/// </summary>
public enum ReportFileValidationFailure
{
    /// <summary>The file passed every check.</summary>
    None = 0,

    /// <summary>The part carries no bytes, so there is nothing to store.</summary>
    EmptyFile,

    /// <summary>The file is larger than <see cref="ReportFileValidator.MaxSizeBytes"/> (FR-REC-05).</summary>
    TooLarge,

    /// <summary>The declared media type is not <c>application/pdf</c>, <c>image/jpeg</c> or <c>image/png</c>.</summary>
    UnsupportedContentType,

    /// <summary>
    /// The declared media type is allowed but the leading bytes are a different format, so the declaration cannot be
    /// trusted. A client controls the <c>Content-Type</c>, the bytes are the evidence.
    /// </summary>
    ContentMismatch
}

namespace MediPassPlus.Application.Records;

/// <summary>
/// The metadata of one report as the history endpoints return it. The stored file name is never exposed: it is an
/// internal storage detail, and the file itself is served only through an endpoint that checks ownership or an
/// active grant (ADR-0005).
/// </summary>
public sealed class ReportMetadataResponse
{
    /// <summary>The report identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The validated media type: <c>application/pdf</c>, <c>image/jpeg</c> or <c>image/png</c>.</summary>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>The stored file size in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>When the file was uploaded, in UTC.</summary>
    public DateTime UploadedAtUtc { get; init; }

    /// <summary>The user account that uploaded the file.</summary>
    public Guid UploadedByUserId { get; init; }
}

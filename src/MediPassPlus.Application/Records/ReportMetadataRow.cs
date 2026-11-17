namespace MediPassPlus.Application.Records;

/// <summary>
/// The metadata of one stored report. The stored file name is deliberately absent: it is an internal storage detail
/// and the file itself is only served through an endpoint that checks ownership or an active grant (ADR-0005).
/// </summary>
/// <param name="Id">The report identifier.</param>
/// <param name="ContentType">The validated media type.</param>
/// <param name="SizeBytes">The stored file size in bytes.</param>
/// <param name="UploadedAtUtc">When the file was uploaded, in UTC.</param>
/// <param name="UploadedByUserId">The user account that uploaded the file.</param>
public sealed record ReportMetadataRow(
    Guid Id,
    string ContentType,
    long SizeBytes,
    DateTime UploadedAtUtc,
    Guid UploadedByUserId);

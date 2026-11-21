namespace MediPassPlus.Application.Records;

/// <summary>
/// The stored report and the owner of the record it belongs to, resolved for the download path. The patient id comes
/// from the record row, never from the request, so authorization is bound to the stored ownership (ADR-0005,
/// ADR-0015).
/// </summary>
/// <param name="ReportId">The report identifier.</param>
/// <param name="MedicalRecordId">The permanent record the report belongs to.</param>
/// <param name="PatientUserId">The user account that owns the record.</param>
/// <param name="StoredFileName">The server-generated storage name.</param>
/// <param name="ContentType">The validated media type of the stored file.</param>
public sealed record ReportDownloadScope(
    Guid ReportId,
    Guid MedicalRecordId,
    Guid PatientUserId,
    string StoredFileName,
    string ContentType);

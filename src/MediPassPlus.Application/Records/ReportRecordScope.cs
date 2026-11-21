namespace MediPassPlus.Application.Records;

/// <summary>
/// The record a report upload is aimed at: its identifier and the user account that owns it. Both come from the stored
/// record row, never from the request, so a caller cannot choose whose record a file lands in (ADR-0005).
/// </summary>
/// <param name="RecordId">The permanent record's identifier.</param>
/// <param name="PatientId">The patient's user account identifier.</param>
public sealed record ReportRecordScope(Guid RecordId, Guid PatientId);

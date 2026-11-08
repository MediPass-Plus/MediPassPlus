namespace MediPassPlus.Domain.Audit;

/// <summary>
/// The controlled audit actions agreed in ADR-0004. Additions require an ADR amendment.
/// </summary>
public enum AuditAction
{
    RecordRead,
    RecordWritten,
    DiagnosisCreated,
    PrescriptionCreated,
    ReportUploaded,
    ReportDownloaded,
    AccessGrantCreated,
    AccessRequestRejected,
    AccessEnded,
    AccessRequestCreated
}

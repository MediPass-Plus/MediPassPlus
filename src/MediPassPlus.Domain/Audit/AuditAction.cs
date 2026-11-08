namespace MediPassPlus.Domain.Audit;

/// <summary>
/// The controlled audit actions agreed in ADR-0004. Additions require an ADR amendment.
/// </summary>
public enum AuditAction
{
    /// <summary>A medical record was read.</summary>
    RecordRead,

    /// <summary>A medical record was written.</summary>
    RecordWritten,

    /// <summary>A diagnosis was created.</summary>
    DiagnosisCreated,

    /// <summary>A prescription was created.</summary>
    PrescriptionCreated,

    /// <summary>A report file was uploaded.</summary>
    ReportUploaded,

    /// <summary>A report file was downloaded.</summary>
    ReportDownloaded,

    /// <summary>An access grant was created after patient approval.</summary>
    AccessGrantCreated,

    /// <summary>A patient rejected an access request.</summary>
    AccessRequestRejected,

    /// <summary>An access grant ended or expired.</summary>
    AccessEnded,

    /// <summary>A doctor created a new access request when starting a visit.</summary>
    AccessRequestCreated
}

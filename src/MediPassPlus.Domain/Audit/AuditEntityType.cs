namespace MediPassPlus.Domain.Audit;

/// <summary>
/// The controlled entity types agreed in ADR-0004. Additions require an ADR amendment.
/// </summary>
public enum AuditEntityType
{
    /// <summary>A patient's medical record.</summary>
    MedicalRecord,

    /// <summary>A diagnosis stored in a medical record.</summary>
    Diagnosis,

    /// <summary>A prescription stored in a medical record.</summary>
    Prescription,

    /// <summary>An uploaded report file.</summary>
    Report,

    /// <summary>A doctor's request for visit-scoped access.</summary>
    AccessRequest,

    /// <summary>A visit-scoped access grant.</summary>
    AccessGrant
}

namespace MediPassPlus.Domain.Audit;

/// <summary>
/// The controlled entity types agreed in ADR-0004. Additions require an ADR amendment.
/// </summary>
public enum AuditEntityType
{
    MedicalRecord,
    Diagnosis,
    Prescription,
    Report,
    AccessRequest,
    AccessGrant
}

namespace MediPassPlus.Application.Records;

/// <summary>
/// Why a diagnosis or prescription write was refused. Every value is mapped to a generic refusal so a caller never
/// learns whether another patient's diagnosis exists (NFR-PRI-01).
/// </summary>
public enum DiagnosisWriteFailure
{
    /// <summary>The write succeeded.</summary>
    None = 0,

    /// <summary>No appointment has the granted identifier.</summary>
    AppointmentNotFound,

    /// <summary>The appointment is not in the visit or in the post-completion window (ADR-0005).</summary>
    AppointmentNotInVisit,

    /// <summary>The grant is no longer active; a write after it ended is refused (FR-REC-10).</summary>
    GrantNotActive,

    /// <summary>The patient's permanent record is missing, which breaks the registration invariant.</summary>
    MedicalRecordNotFound,

    /// <summary>The diagnosis does not belong to the granted appointment and patient.</summary>
    ScopeMismatch
}

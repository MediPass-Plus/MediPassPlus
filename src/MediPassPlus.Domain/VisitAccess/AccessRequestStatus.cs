namespace MediPassPlus.Domain.VisitAccess;

/// <summary>
/// Lifecycle of a doctor's request to access one patient's record for one appointment.
/// </summary>
public enum AccessRequestStatus
{
    /// <summary>The request is waiting for the patient's decision.</summary>
    Pending = 0,

    /// <summary>The patient approved the request and an access grant was created.</summary>
    Approved = 1,

    /// <summary>The patient rejected the request; no grant exists.</summary>
    Rejected = 2
}

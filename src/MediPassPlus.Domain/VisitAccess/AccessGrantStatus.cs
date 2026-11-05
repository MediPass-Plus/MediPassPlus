namespace MediPassPlus.Domain.VisitAccess;

/// <summary>
/// Effective state of an access grant at a point in time. It is derived from the grant timestamps and is never stored.
/// </summary>
public enum AccessGrantStatus
{
    /// <summary>The grant has not reached its effective end and access is allowed.</summary>
    Active = 0,

    /// <summary>The visit was completed or the patient ended the grant, and the effective end has passed.</summary>
    Ended = 1,

    /// <summary>The two-hour limit was reached without a deliberate end.</summary>
    Expired = 2
}

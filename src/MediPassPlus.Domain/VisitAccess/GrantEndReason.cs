namespace MediPassPlus.Domain.VisitAccess;

/// <summary>
/// Why an access grant stopped, or is scheduled to stop, being valid.
/// <see cref="TimeLimit"/> is derived when the two-hour limit is reached without a stored reason.
/// </summary>
public enum GrantEndReason
{
    /// <summary>The doctor completed the visit; access continues for the 15-minute grace window.</summary>
    VisitCompleted = 0,

    /// <summary>The two-hour limit was reached without an earlier end.</summary>
    TimeLimit = 1,

    /// <summary>The patient ended the access immediately.</summary>
    PatientEnded = 2
}

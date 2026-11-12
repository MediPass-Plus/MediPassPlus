namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The outcome of atomically recording a wrong six-digit entry. The store applies the increment and the lock in one
/// conditional database update, so a concurrent entry cannot observe or overwrite a stale counter (FR-ACS-05).
/// </summary>
public enum AccessCodeFailedAttemptOutcome
{
    /// <summary>The wrong entry was counted; the credential may have just been locked by it.</summary>
    Recorded = 0,

    /// <summary>The credential was already locked, so this entry was not counted.</summary>
    AlreadyLocked,

    /// <summary>The credential is missing, consumed, revoked or expired, so this entry was not counted.</summary>
    Unavailable
}

namespace MediPassPlus.Application.Records;

/// <summary>
/// Which side of the record the caller is on. The role is taken from the authenticated identity, never from the
/// request body (FR-ACS-06).
/// </summary>
public enum ReportUploaderRole
{
    /// <summary>A patient uploading to their own record.</summary>
    Patient = 0,

    /// <summary>A doctor uploading while holding an active access grant for the patient.</summary>
    Doctor = 1
}

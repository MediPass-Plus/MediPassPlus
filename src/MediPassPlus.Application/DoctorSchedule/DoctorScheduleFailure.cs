using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.DoctorSchedule;

/// <summary>
/// Why a doctor schedule request was refused.
/// </summary>
public enum DoctorScheduleFailure
{
    /// <summary>The schedule was returned.</summary>
    None = 0,

    /// <summary>The authenticated user has no doctor profile.</summary>
    DoctorNotFound
}

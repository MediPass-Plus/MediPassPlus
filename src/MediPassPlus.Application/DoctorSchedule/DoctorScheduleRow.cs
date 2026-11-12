using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.DoctorSchedule;

/// <summary>
/// A row read from the database before it is mapped to the public schedule DTO.
/// </summary>
/// <param name="AppointmentId">The appointment identifier.</param>
/// <param name="StartTimeUtc">The slot start, in UTC.</param>
/// <param name="EndTimeUtc">The slot end, in UTC.</param>
/// <param name="PatientName">The patient's full name.</param>
/// <param name="Status">The stored appointment status.</param>
public sealed record DoctorScheduleRow(
    int AppointmentId,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    string PatientName,
    AppointmentStatus Status);

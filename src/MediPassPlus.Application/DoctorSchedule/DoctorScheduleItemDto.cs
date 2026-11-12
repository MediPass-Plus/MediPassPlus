namespace MediPassPlus.Application.DoctorSchedule;

/// <summary>
/// One appointment on the doctor's daily schedule. It exposes only the minimum patient field the doctor needs:
/// the patient's name (US-10, FR-BKG-07).
/// </summary>
/// <param name="AppointmentId">The appointment identifier.</param>
/// <param name="StartTimeUtc">The slot start, in UTC.</param>
/// <param name="EndTimeUtc">The slot end, in UTC.</param>
/// <param name="PatientName">The patient's full name.</param>
/// <param name="Status">The appointment status as its stable name.</param>
public sealed record DoctorScheduleItemDto(
    int AppointmentId,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    string PatientName,
    string Status);

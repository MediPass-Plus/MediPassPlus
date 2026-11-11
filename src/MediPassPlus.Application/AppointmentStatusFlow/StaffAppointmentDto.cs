namespace MediPassPlus.Application.AppointmentStatusFlow;

/// <summary>
/// The appointment view returned to clinic staff after a check-in. It contains only the fields clinic staff are
/// allowed to see: patient name, phone, appointment time and status (FR-STF-04).
/// </summary>
/// <param name="AppointmentId">The appointment identifier.</param>
/// <param name="PatientName">The patient's full name.</param>
/// <param name="PatientPhone">The patient's phone number, when one is stored.</param>
/// <param name="StartTimeUtc">The slot start, in UTC.</param>
/// <param name="EndTimeUtc">The slot end, in UTC.</param>
/// <param name="Status">The appointment status as its stable name.</param>
/// <param name="CheckedInAtUtc">When the patient was marked as arrived, in UTC.</param>
public sealed record StaffAppointmentDto(
    int AppointmentId,
    string PatientName,
    string? PatientPhone,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    string Status,
    DateTime CheckedInAtUtc);

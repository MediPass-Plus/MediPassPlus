namespace MediPassPlus.Application.Booking;

/// <summary>
/// One appointment in the authenticated patient's own list. It exposes the date, doctor, clinic and status that
/// US-09 asks for, and never exposes another patient's data (FR-BKG-06).
/// </summary>
/// <param name="Id">The appointment identifier.</param>
/// <param name="DoctorId">The doctor who owns the appointment.</param>
/// <param name="DoctorName">The doctor's full name.</param>
/// <param name="ClinicId">The clinic where the appointment takes place.</param>
/// <param name="ClinicName">The clinic's name.</param>
/// <param name="DoctorSlotId">The slot the appointment currently occupies.</param>
/// <param name="StartTimeUtc">The slot start, in UTC.</param>
/// <param name="EndTimeUtc">The slot end, in UTC.</param>
/// <param name="Status">The appointment status as its stable name.</param>
/// <param name="CreatedAtUtc">When the appointment was created, in UTC.</param>
public sealed record PatientAppointmentDto(
    int Id,
    int DoctorId,
    string DoctorName,
    int ClinicId,
    string ClinicName,
    int DoctorSlotId,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    string Status,
    DateTime CreatedAtUtc);

using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.Booking;

/// <summary>
/// One row read for the authenticated patient's appointment list. The query joins the doctor, the doctor's user
/// account and the clinic so the API can show the date, doctor, clinic and status US-09 asks for (FR-BKG-06).
/// </summary>
/// <param name="Id">The appointment identifier.</param>
/// <param name="DoctorId">The doctor who owns the appointment.</param>
/// <param name="DoctorName">The doctor's full name.</param>
/// <param name="ClinicId">The clinic where the appointment takes place.</param>
/// <param name="ClinicName">The clinic's name.</param>
/// <param name="DoctorSlotId">The slot the appointment currently occupies.</param>
/// <param name="StartTimeUtc">The slot start, in UTC.</param>
/// <param name="EndTimeUtc">The slot end, in UTC.</param>
/// <param name="Status">The appointment status.</param>
/// <param name="CreatedAtUtc">When the appointment was created, in UTC.</param>
public sealed record PatientAppointmentRow(
    int Id,
    int DoctorId,
    string DoctorName,
    int ClinicId,
    string ClinicName,
    int DoctorSlotId,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    AppointmentStatus Status,
    DateTime CreatedAtUtc);

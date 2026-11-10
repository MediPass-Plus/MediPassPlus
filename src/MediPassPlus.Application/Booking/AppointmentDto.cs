namespace MediPassPlus.Application.Booking;

/// <summary>
/// The appointment returned after a successful booking, cancellation or reschedule. The patient, doctor and
/// appointment identifiers are included so the caller can verify that the operation stayed inside the original scope.
/// </summary>
/// <param name="Id">The appointment identifier.</param>
/// <param name="PatientId">The patient's user account identifier.</param>
/// <param name="DoctorId">The doctor who owns the appointment.</param>
/// <param name="DoctorSlotId">The slot the appointment currently occupies.</param>
/// <param name="Status">The appointment status as its stable name.</param>
/// <param name="StartTimeUtc">The slot start, in UTC.</param>
/// <param name="EndTimeUtc">The slot end, in UTC.</param>
/// <param name="CreatedAtUtc">When the appointment was created, in UTC.</param>
public sealed record AppointmentDto(
    int Id,
    Guid PatientId,
    int DoctorId,
    int DoctorSlotId,
    string Status,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    DateTime CreatedAtUtc);

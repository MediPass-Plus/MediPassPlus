namespace MediPassPlus.Application.AppointmentStatusFlow;

/// <summary>
/// The result of a doctor completion. It carries only the appointment identifier, the new status and the completion
/// timestamp so the response stays minimal.
/// </summary>
/// <param name="AppointmentId">The appointment identifier.</param>
/// <param name="Status">The appointment status as its stable name.</param>
/// <param name="CompletedAtUtc">When the visit was completed, in UTC.</param>
public sealed record DoctorAppointmentStatusDto(
    int AppointmentId,
    string Status,
    DateTime CompletedAtUtc);

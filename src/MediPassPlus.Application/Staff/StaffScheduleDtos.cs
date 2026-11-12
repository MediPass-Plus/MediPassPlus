namespace MediPassPlus.Application.Staff;

// Only the fields the reception needs. No diagnosis, prescription or report data (FR-PRV-06).
public record StaffScheduleItemDto(
    int AppointmentId,
    string PatientName,
    string? PatientPhone,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    string DoctorName,
    string Status);
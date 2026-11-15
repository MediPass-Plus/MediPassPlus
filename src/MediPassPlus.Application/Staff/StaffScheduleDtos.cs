namespace MediPassPlus.Application.Staff;

// Only the fields the reception needs (FR-STF-04): patient name, phone, appointment time and status.
// AppointmentId is kept because check-in needs it. No diagnosis, prescription or report data (FR-PRV-06).
public record StaffScheduleItemDto(
    int AppointmentId,
    string PatientName,
    string? PatientPhone,
    DateTime StartTimeUtc,
    string Status);
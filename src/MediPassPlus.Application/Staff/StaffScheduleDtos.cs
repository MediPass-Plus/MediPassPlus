namespace MediPassPlus.Application.Staff;

// The four FR-STF-04 patient-data fields: patient name, phone, appointment time and status. AppointmentId is the
// opaque key the staff check-in action needs (ADR-0014). No diagnosis, prescription or report data (FR-PRV-06).
public record StaffScheduleItemDto(
    int AppointmentId,
    string PatientName,
    string? PatientPhone,
    DateTime StartTimeUtc,
    string Status);
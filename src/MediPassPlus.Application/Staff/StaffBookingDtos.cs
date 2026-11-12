namespace MediPassPlus.Application.Staff;

public record StaffBookAppointmentDto(string PatientEmail, int DoctorId, int SlotId);
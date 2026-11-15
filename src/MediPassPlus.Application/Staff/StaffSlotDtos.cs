namespace MediPassPlus.Application.Staff;

public record CreateStaffSlotDto(DateTime StartTimeUtc, DateTime EndTimeUtc);

public record StaffSlotDto(int Id, int DoctorId, DateTime StartTimeUtc, DateTime EndTimeUtc, bool IsAvailable);
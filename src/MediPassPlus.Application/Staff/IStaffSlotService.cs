namespace MediPassPlus.Application.Staff;

public enum StaffSlotFailure
{
    None,
    StaffProfileNotFound,
    DoctorNotFound,
    SlotNotFound,
    SlotInPast,
    SlotOverlaps,
    SlotAlreadyBooked
}

public sealed record StaffSlotResult(StaffSlotDto? Slot, StaffSlotFailure Failure)
{
    public bool IsSuccess => Failure == StaffSlotFailure.None;
}

public interface IStaffSlotService
{
    Task<StaffSlotResult> CreateAsync(
        Guid staffUserId, int doctorId, CreateStaffSlotDto request, CancellationToken cancellationToken);

    Task<StaffSlotResult> DeleteAsync(
        Guid staffUserId, int doctorId, int slotId, CancellationToken cancellationToken);
}
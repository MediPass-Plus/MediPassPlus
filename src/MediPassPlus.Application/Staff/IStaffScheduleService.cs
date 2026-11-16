namespace MediPassPlus.Application.Staff;

public enum StaffScheduleFailure
{
    None,
    StaffProfileNotFound
}

public sealed record StaffScheduleResult(
    IReadOnlyList<StaffScheduleItemDto> Items,
    StaffScheduleFailure Failure)
{
    public bool IsSuccess => Failure == StaffScheduleFailure.None;
}

public interface IStaffScheduleService
{
    // Returns the appointments of one day for the clinic of the authenticated staff member.
    // The clinic is read from the ClinicStaff row of the user, never from the request.
    Task<StaffScheduleResult> GetScheduleAsync(
        Guid staffUserId,
        DateOnly date,
        CancellationToken cancellationToken);
}
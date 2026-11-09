namespace MediPassPlus.Application.AdminManagement;

public enum ChangeStatusFailure
{
    None,
    UserNotFound,
    CannotChangeSelf
}

public interface IAccountStatusService
{
    // Sets IsActive for the target account. The admin cannot change their own status.
    Task<ChangeStatusFailure> SetActiveAsync(
        Guid targetUserId,
        bool isActive,
        Guid currentAdminId,
        CancellationToken cancellationToken);
}
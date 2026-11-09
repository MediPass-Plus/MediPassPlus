using MediPassPlus.Application.AdminManagement;
using MediPassPlus.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace MediPassPlus.Infrastructure.AdminManagement;

public class AccountStatusService : IAccountStatusService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountStatusService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<ChangeStatusFailure> SetActiveAsync(
        Guid targetUserId,
        bool isActive,
        Guid currentAdminId,
        CancellationToken cancellationToken)
    {
        if (targetUserId == currentAdminId)
        {
            return ChangeStatusFailure.CannotChangeSelf;
        }

        var user = await _userManager.FindByIdAsync(targetUserId.ToString());
        if (user is null)
        {
            return ChangeStatusFailure.UserNotFound;
        }

        if (user.IsActive != isActive)
        {
            user.IsActive = isActive;
            await _userManager.UpdateAsync(user);
        }

        return ChangeStatusFailure.None;
    }
}
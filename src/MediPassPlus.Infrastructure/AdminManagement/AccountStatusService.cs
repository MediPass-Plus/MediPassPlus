using MediPassPlus.Application.AdminManagement;
using MediPassPlus.Application.Auth;
using MediPassPlus.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace MediPassPlus.Infrastructure.AdminManagement;

public class AccountStatusService : IAccountStatusService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthTokenService _authTokens;

    public AccountStatusService(UserManager<ApplicationUser> userManager, IAuthTokenService authTokens)
    {
        _userManager = userManager;
        _authTokens = authTokens;
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

            // Deactivation is the admin's kill switch: every live session of the account ends with it, so
            // reactivating the account cannot bring an old refresh token back to life (FR-STF-07, FR-ACC-06).
            if (!isActive)
            {
                await _authTokens.RevokeAllForUserAsync(user.Id, cancellationToken);
            }
        }

        return ChangeStatusFailure.None;
    }
}
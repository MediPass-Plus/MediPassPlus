using System.ComponentModel.DataAnnotations;

namespace MediPassPlus.Api.RateLimiting;

/// <summary>
/// Numeric limits for the <c>ChangePassword</c> policy: how often one authenticated account may try to replace its
/// password in one fixed window. The budget exists because <c>UserManager.ChangePasswordAsync</c> does not raise the
/// lockout counter, so a stolen access token could otherwise guess the current password without any brake. The numbers
/// are configuration and may be tuned after the security review the ADR records as pending.
/// </summary>
public sealed class ChangePasswordRateLimitOptions
{
    /// <summary>The configuration section that binds these options.</summary>
    public const string SectionName = "RateLimiting:ChangePassword";

    /// <summary>Gets or sets how many attempts one account may make in one window. Defaults to 5.</summary>
    [Range(1, 1000)]
    public int PermitLimit { get; set; } = 5;

    /// <summary>Gets or sets the fixed window length in minutes. Defaults to 15.</summary>
    [Range(1, 1440)]
    public int WindowMinutes { get; set; } = 15;
}

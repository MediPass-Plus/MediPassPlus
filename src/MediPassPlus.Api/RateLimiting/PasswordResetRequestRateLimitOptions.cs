using System.ComponentModel.DataAnnotations;

namespace MediPassPlus.Api.RateLimiting;

/// <summary>
/// Numeric limits for the <c>PasswordResetRequest</c> policy: how often one email address may start the password-reset
/// flow from one client address in a fixed window. The budget exists because the endpoint sends an email and generates
/// a reset token for every active account it names, so an unbounded caller could use it to flood a mailbox
/// (FR-PRV-07). The answer to a caller under the budget stays the same whether the address exists or not; only the
/// caller's own attempt count decides the <c>429</c>. The numbers are configuration and may be tuned after the security
/// review the ADR records as pending.
/// </summary>
public sealed class PasswordResetRequestRateLimitOptions
{
    /// <summary>The configuration section that binds these options.</summary>
    public const string SectionName = "RateLimiting:PasswordResetRequest";

    /// <summary>Gets or sets how many reset requests one email and client address may make in one window.</summary>
    [Range(1, 1000)]
    public int PermitLimit { get; set; } = 3;

    /// <summary>Gets or sets the fixed window length in minutes. Defaults to 15.</summary>
    [Range(1, 1440)]
    public int WindowMinutes { get; set; } = 15;
}

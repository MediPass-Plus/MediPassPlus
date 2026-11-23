using System.ComponentModel.DataAnnotations;

namespace MediPassPlus.Api.RateLimiting;

/// <summary>
/// Numeric limits for the <c>PasswordResetSubmit</c> policy: how often one email address may try to complete a
/// password reset from one client address in a fixed window (FR-PRV-07). The reset token is high-entropy, so this is
/// not a guessing brake; it bounds a caller that keeps submitting tokens and makes the endpoint as expensive to abuse
/// as every other anonymous credential path. The numbers are configuration and may be tuned after the security review
/// the ADR records as pending.
/// </summary>
public sealed class PasswordResetSubmitRateLimitOptions
{
    /// <summary>The configuration section that binds these options.</summary>
    public const string SectionName = "RateLimiting:PasswordResetSubmit";

    /// <summary>Gets or sets how many reset completions one email and client address may try in one window.</summary>
    [Range(1, 1000)]
    public int PermitLimit { get; set; } = 5;

    /// <summary>Gets or sets the fixed window length in minutes. Defaults to 15.</summary>
    [Range(1, 1440)]
    public int WindowMinutes { get; set; } = 15;
}

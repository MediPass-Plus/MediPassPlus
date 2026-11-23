using System.ComponentModel.DataAnnotations;

namespace MediPassPlus.Api.RateLimiting;

/// <summary>
/// Numeric limits for the <c>Login</c> policy: how many sign-in attempts one email address may make from one client
/// address in a fixed window (ADR-0007 decision 2, FR-PRV-07). The per-account lockout stays the authoritative
/// FR-ACS-05 rule; this budget is defence-in-depth in front of it. The numbers are configuration and may be tuned
/// after the security review the ADR records as pending.
/// </summary>
public sealed class LoginRateLimitOptions
{
    /// <summary>The configuration section that binds these options.</summary>
    public const string SectionName = "RateLimiting:Login";

    /// <summary>Gets or sets how many sign-in attempts one email and client address may make in one window.</summary>
    [Range(1, 1000)]
    public int PermitLimit { get; set; } = 5;

    /// <summary>Gets or sets the fixed window length in minutes. Defaults to 15.</summary>
    [Range(1, 1440)]
    public int WindowMinutes { get; set; } = 15;
}

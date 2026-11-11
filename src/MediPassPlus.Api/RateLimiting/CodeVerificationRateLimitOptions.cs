using System.ComponentModel.DataAnnotations;

namespace MediPassPlus.Api.RateLimiting;

/// <summary>
/// Numeric limits for the <c>CodeVerification</c> policy: how often one doctor may try a six-digit code for one
/// appointment in a fixed window (ADR-0007 decision 2, FR-PRV-07). The per-credential lockout remains the
/// authoritative FR-ACS-05 rule; this limit is defence-in-depth.
/// </summary>
public sealed class CodeVerificationRateLimitOptions
{
    /// <summary>The configuration section that binds these options.</summary>
    public const string SectionName = "RateLimiting:CodeVerification";

    /// <summary>Gets or sets how many verification attempts one doctor and appointment may make in one window.</summary>
    [Range(1, 1000)]
    public int PermitLimit { get; set; } = 5;

    /// <summary>Gets or sets the fixed window length in minutes. Defaults to 15.</summary>
    [Range(1, 1440)]
    public int WindowMinutes { get; set; } = 15;
}

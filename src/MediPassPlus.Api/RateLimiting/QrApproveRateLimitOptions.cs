using System.ComponentModel.DataAnnotations;

namespace MediPassPlus.Api.RateLimiting;

/// <summary>
/// Numeric limits for the <c>QrApprove</c> policy. The policy limits a patient's approve/reject attempts with a
/// fixed window (ADR-0007 decision 2). The values are configuration, not requirement constants, and may be tuned
/// after the security review the ADR records as pending.
/// </summary>
public sealed class QrApproveRateLimitOptions
{
    /// <summary>The configuration section that binds these options.</summary>
    public const string SectionName = "RateLimiting:QrApprove";

    /// <summary>
    /// Gets or sets how many approve/reject attempts a patient may make in one window. Defaults to 5, matching the
    /// proposal's verification limit.
    /// </summary>
    [Range(1, 1000)]
    public int PermitLimit { get; set; } = 5;

    /// <summary>Gets or sets the fixed window length in minutes. Defaults to 15.</summary>
    [Range(1, 1440)]
    public int WindowMinutes { get; set; } = 15;
}

using System.ComponentModel.DataAnnotations;

namespace MediPassPlus.Api.RateLimiting;

/// <summary>
/// Numeric limits for the <c>CodeGeneration</c> policy: how often a patient may request a six-digit fallback in one
/// fixed window (ADR-0007 decision 2, FR-PRV-07). The numbers are configuration and may be tuned after the security
/// review the ADR records as pending.
/// </summary>
public sealed class CodeGenerationRateLimitOptions
{
    /// <summary>The configuration section that binds these options.</summary>
    public const string SectionName = "RateLimiting:CodeGeneration";

    /// <summary>Gets or sets how many codes a patient may request in one window. Defaults to 3.</summary>
    [Range(1, 1000)]
    public int PermitLimit { get; set; } = 3;

    /// <summary>Gets or sets the fixed window length in minutes. Defaults to 15.</summary>
    [Range(1, 1440)]
    public int WindowMinutes { get; set; } = 15;
}

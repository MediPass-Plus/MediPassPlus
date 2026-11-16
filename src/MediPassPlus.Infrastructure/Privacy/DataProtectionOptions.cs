namespace MediPassPlus.Infrastructure.Privacy;

/// <summary>
/// Binds the <c>DataProtection</c> configuration section. <see cref="KeysPath"/> is the directory of the
/// application's Data Protection key ring: it must be absolute and outside the repository (ADR-0007 decision 5).
/// It is an infrastructure path, not a secret, and is supplied per deployment.
/// </summary>
public sealed class DataProtectionOptions
{
    /// <summary>The configuration section that binds these options.</summary>
    public const string SectionName = "DataProtection";

    /// <summary>
    /// Gets or sets the absolute directory that holds the key ring. Empty means "not configured": Development and
    /// Testing fall back to <see cref="DataProtectionKeyPath.DefaultDevelopmentPath"/>, while Production refuses to
    /// start (ADR-0007 decision 5).
    /// </summary>
    public string KeysPath { get; set; } = string.Empty;
}

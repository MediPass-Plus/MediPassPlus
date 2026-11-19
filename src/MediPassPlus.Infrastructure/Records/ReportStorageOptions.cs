namespace MediPassPlus.Infrastructure.Records;

/// <summary>
/// Binds the <c>Reports:Storage</c> configuration section. <see cref="RootPath"/> is the directory that holds uploaded
/// report files: it must be absolute and outside the repository and outside <c>wwwroot</c>, because a file under the
/// static root is a public link (FR-PRV-05, ADR-0015 decision 6).
/// </summary>
public sealed class ReportStorageOptions
{
    /// <summary>The configuration section that binds these options.</summary>
    public const string SectionName = "Reports:Storage";

    /// <summary>
    /// Gets or sets the absolute directory that holds the uploaded files. Empty means "not configured": Development
    /// and Testing fall back to <see cref="ReportStoragePath.DefaultDevelopmentPath"/>, while Production refuses to
    /// start, exactly as the Data Protection key ring does (ADR-0007 decision 5).
    /// </summary>
    public string RootPath { get; set; } = string.Empty;
}

using System.IO;
using MediPassPlus.Infrastructure.Privacy;

namespace MediPassPlus.Infrastructure.Records;

/// <summary>
/// Resolves the report storage directory and decides whether a directory would put patient files where they must never
/// be: inside the checkout (and therefore in Git) or inside <c>wwwroot</c> (and therefore behind a public link)
/// (FR-PRV-05, ADR-0015 decision 6).
/// </summary>
public static class ReportStoragePath
{
    /// <summary>The static-content directory. Nothing stored here needs authorization, which is the whole problem.</summary>
    public const string WebRootDirectoryName = "wwwroot";

    private const string DefaultDirectoryName = "medipass-report-storage";

    /// <summary>
    /// The fallback directory for Development and Testing: a directory under the operating system's temporary path, so
    /// a fresh clone starts without local configuration and no patient file lands in the repository.
    /// </summary>
    public static string DefaultDevelopmentPath { get; } =
        Path.Combine(Path.GetTempPath(), DefaultDirectoryName);

    /// <summary>
    /// Returns the absolute directory that holds the uploaded files: the configured directory when one is supplied,
    /// otherwise <see cref="DefaultDevelopmentPath"/>.
    /// </summary>
    /// <param name="configuredPath">The configured <c>Reports:Storage:RootPath</c>, possibly null or empty.</param>
    /// <returns>An absolute directory path. The directory is not created here.</returns>
    public static string Resolve(string? configuredPath) =>
        string.IsNullOrWhiteSpace(configuredPath)
            ? DefaultDevelopmentPath
            : Path.GetFullPath(configuredPath.Trim());

    /// <summary>
    /// Reports whether the directory is <c>wwwroot</c> or lies under it.
    /// </summary>
    /// <param name="fullPath">The storage directory, already required to be absolute by the caller.</param>
    /// <param name="contentRootPath">The host content root, which holds <c>wwwroot</c> in every ASP.NET Core layout.</param>
    /// <returns>True when the directory must be refused.</returns>
    public static bool IsInsideWebRoot(string fullPath, string? contentRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        if (string.IsNullOrWhiteSpace(contentRootPath))
        {
            return false;
        }

        var candidate = Path.GetFullPath(fullPath);
        var webRoot = Path.GetFullPath(Path.Combine(contentRootPath, WebRootDirectoryName));

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(candidate, webRoot, comparison)
            || candidate.StartsWith(EnsureTrailingSeparator(webRoot), comparison);
    }

    /// <summary>
    /// Reports whether the directory must be refused: inside the repository or the application directory, or inside
    /// <c>wwwroot</c>.
    /// </summary>
    /// <param name="fullPath">The storage directory, already required to be absolute by the caller.</param>
    /// <param name="contentRootPath">The host content root.</param>
    /// <returns>True when the directory must be refused.</returns>
    /// <remarks>
    /// The checkout rule is the same one the Data Protection key ring obeys, so it reuses that tested implementation
    /// (<see cref="DataProtectionKeyPath.IsInsideRepository"/>) instead of walking the tree a second time. The
    /// <c>wwwroot</c> rule is separate and named, because "a file under the static root is a public link" is the
    /// failure this layer has to prevent, not an incidental effect of living in the checkout.
    /// </remarks>
    public static bool IsRefused(string fullPath, string? contentRootPath) =>
        IsInsideWebRoot(fullPath, contentRootPath)
        || DataProtectionKeyPath.IsInsideRepository(fullPath, contentRootPath);

    private static string EnsureTrailingSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
}

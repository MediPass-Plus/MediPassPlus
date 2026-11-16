namespace MediPassPlus.Infrastructure.Privacy;

/// <summary>
/// Resolves the key-ring directory and decides whether a directory would put the keys inside the repository.
/// Keys are never written into the checkout (FR-PRV-04, NFR-SEC-05, ADR-0007 decision 5), so a relative path or a
/// path under the repository root or the application directory is refused.
/// </summary>
public static class DataProtectionKeyPath
{
    /// <summary>The solution file that marks the repository root.</summary>
    public const string RepositoryMarkerFileName = "MediPassPlus.slnx";

    /// <summary>The version-control marker that identifies the repository root, as a directory or a file.</summary>
    public const string RepositoryMarkerDirectoryName = ".git";

    private const string DefaultDirectoryName = "medipass-dataprotection-keys";

    /// <summary>
    /// The fallback ring directory for Development and Testing: a directory under the operating system's temporary
    /// path. A fresh clone therefore starts without local configuration, and no key is written into the repository.
    /// </summary>
    public static string DefaultDevelopmentPath { get; } =
        Path.Combine(Path.GetTempPath(), DefaultDirectoryName);

    /// <summary>
    /// Returns the absolute directory that holds the key ring: the configured directory when one is supplied,
    /// otherwise <see cref="DefaultDevelopmentPath"/>.
    /// </summary>
    /// <param name="configuredPath">The configured <c>DataProtection:KeysPath</c>, possibly null or empty.</param>
    /// <returns>An absolute directory path. The directory is not created here.</returns>
    public static string Resolve(string? configuredPath) =>
        string.IsNullOrWhiteSpace(configuredPath)
            ? DefaultDevelopmentPath
            : Path.GetFullPath(configuredPath.Trim());

    /// <summary>
    /// Reports whether the directory is inside the repository, the application content root or the application
    /// directory, which are all locations where keys must never be written.
    /// </summary>
    /// <param name="fullPath">The key-ring directory, already required to be absolute by the caller.</param>
    /// <param name="contentRootPath">The host content root, used to locate the repository root.</param>
    /// <returns>True when the directory must be refused.</returns>
    public static bool IsInsideRepository(string fullPath, string? contentRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        var candidate = Path.GetFullPath(fullPath);
        var repositoryRoot = FindRepositoryRoot(contentRootPath);

        if (repositoryRoot is not null && IsWithin(candidate, repositoryRoot))
        {
            return true;
        }

        return IsWithin(candidate, contentRootPath) || IsWithin(candidate, AppContext.BaseDirectory);
    }

    private static string? FindRepositoryRoot(string? contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(contentRootPath))
        {
            return null;
        }

        var directory = new DirectoryInfo(Path.GetFullPath(contentRootPath));

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, RepositoryMarkerFileName))
                || Directory.Exists(Path.Combine(directory.FullName, RepositoryMarkerDirectoryName))
                || File.Exists(Path.Combine(directory.FullName, RepositoryMarkerDirectoryName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool IsWithin(string candidate, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

        return string.Equals(candidate, parent, comparison)
            || candidate.StartsWith(EnsureTrailingSeparator(parent), comparison);
    }

    private static string EnsureTrailingSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
}

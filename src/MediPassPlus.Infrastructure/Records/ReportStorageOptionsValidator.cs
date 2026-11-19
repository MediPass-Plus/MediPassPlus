using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MediPassPlus.Infrastructure.Records;

/// <summary>
/// Fails closed on the report storage configuration: Production must name an absolute directory, and no environment
/// may keep patient files inside the repository or behind <c>wwwroot</c> (FR-PRV-05, NFR-SEC-09, ADR-0015 decision 6).
/// </summary>
public sealed class ReportStorageOptionsValidator : IValidateOptions<ReportStorageOptions>
{
    private readonly IHostEnvironment _environment;

    /// <summary>Creates the validator for the running host environment.</summary>
    /// <param name="environment">The host environment that decides whether the path is mandatory.</param>
    /// <exception cref="ArgumentNullException">The environment is null.</exception>
    public ReportStorageOptionsValidator(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ReportStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            return IsProduction
                ? ValidateOptionsResult.Fail(
                    "Reports:Storage:RootPath is required in Production. Point it at an absolute directory outside "
                    + "the repository and outside wwwroot so uploaded files are never committed or publicly served "
                    + "(ADR-0015 decision 6).")
                : ValidateOptionsResult.Success;
        }

        if (!Path.IsPathFullyQualified(options.RootPath))
        {
            return ValidateOptionsResult.Fail(
                "Reports:Storage:RootPath must be an absolute directory outside the repository and outside wwwroot; "
                + "a relative path resolves inside the checkout during development (ADR-0015 decision 6).");
        }

        if (ReportStoragePath.IsInsideWebRoot(options.RootPath, _environment.ContentRootPath))
        {
            return ValidateOptionsResult.Fail(
                "Reports:Storage:RootPath points inside wwwroot, where static files are served without any grant "
                + "check. Choose a directory outside the web root (FR-PRV-05, ADR-0015 decision 6).");
        }

        if (ReportStoragePath.IsRefused(options.RootPath, _environment.ContentRootPath))
        {
            return ValidateOptionsResult.Fail(
                "Reports:Storage:RootPath points inside the repository or the application directory. Choose a "
                + "directory outside the checkout so patient files are never committed (ADR-0015 decision 6).");
        }

        return ValidateOptionsResult.Success;
    }

    private bool IsProduction =>
        string.Equals(_environment.EnvironmentName, Environments.Production, StringComparison.OrdinalIgnoreCase);
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MediPassPlus.Infrastructure.Privacy;

/// <summary>
/// Fails closed on the key-ring configuration: Production must name an absolute directory, and no environment may
/// keep the keys inside the repository or the application directory (FR-PRV-04, NFR-SEC-05, ADR-0007 decision 5).
/// </summary>
public sealed class DataProtectionOptionsValidator : IValidateOptions<DataProtectionOptions>
{
    private readonly IHostEnvironment _environment;

    /// <summary>Creates the validator for the running host environment.</summary>
    /// <param name="environment">The host environment that decides whether the path is mandatory.</param>
    /// <exception cref="ArgumentNullException">The environment is null.</exception>
    public DataProtectionOptionsValidator(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DataProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.KeysPath))
        {
            return IsProduction
                ? ValidateOptionsResult.Fail(
                    "DataProtection:KeysPath is required in Production. Point it at an absolute directory outside "
                    + "the repository so the diagnosis key ring survives a restart (ADR-0007 decision 5).")
                : ValidateOptionsResult.Success;
        }

        if (!Path.IsPathFullyQualified(options.KeysPath))
        {
            return ValidateOptionsResult.Fail(
                "DataProtection:KeysPath must be an absolute directory outside the repository; a relative path "
                + "resolves inside the checkout during development (ADR-0007 decision 5).");
        }

        if (DataProtectionKeyPath.IsInsideRepository(options.KeysPath, _environment.ContentRootPath))
        {
            return ValidateOptionsResult.Fail(
                "DataProtection:KeysPath points inside the repository or the application directory. Choose a "
                + "directory outside the checkout so keys are never committed (ADR-0007 decision 5).");
        }

        return ValidateOptionsResult.Success;
    }

    private bool IsProduction =>
        string.Equals(_environment.EnvironmentName, Environments.Production, StringComparison.OrdinalIgnoreCase);
}

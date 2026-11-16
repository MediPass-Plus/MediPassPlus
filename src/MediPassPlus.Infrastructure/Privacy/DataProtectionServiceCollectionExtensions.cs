using MediPassPlus.Application.Privacy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MediPassPlus.Infrastructure.Privacy;

/// <summary>
/// Registers the application's Data Protection key ring and the single diagnosis-text encryptor (FR-PRV-04,
/// NFR-SEC-05, ADR-0007 decision 5).
/// </summary>
public static class DataProtectionServiceCollectionExtensions
{
    /// <summary>The application name that scopes the key ring to this application.</summary>
    public const string ApplicationName = "MediPassPlus";

    /// <summary>
    /// Persists the Data Protection ring to <c>DataProtection:KeysPath</c> and registers
    /// <see cref="IDiagnosisProtector"/>. The configuration is validated at startup, so Production refuses to run
    /// without an absolute directory outside the repository.
    /// </summary>
    /// <param name="services">The service collection to add the services to.</param>
    /// <param name="configuration">The application configuration that supplies the key-ring directory.</param>
    /// <param name="environment">The host environment that decides whether the directory is mandatory.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IServiceCollection AddDiagnosisProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddOptions<DataProtectionOptions>()
            .Bind(configuration.GetSection(DataProtectionOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<DataProtectionOptions>, DataProtectionOptionsValidator>();

        // The ring is file-based: no database and no migration. PersistKeysToFileSystem creates the directory on
        // the first key generation, so registering the services stays free of I/O.
        var keysPath = DataProtectionKeyPath.Resolve(
            configuration[$"{DataProtectionOptions.SectionName}:{nameof(DataProtectionOptions.KeysPath)}"]);

        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        services.AddSingleton<IDiagnosisProtector, DiagnosisProtector>();

        return services;
    }
}

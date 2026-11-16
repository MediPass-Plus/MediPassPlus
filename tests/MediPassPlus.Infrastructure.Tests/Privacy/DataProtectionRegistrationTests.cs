using MediPassPlus.Application.Privacy;
using MediPassPlus.Infrastructure.Privacy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MediPassPlus.Infrastructure.Tests.Privacy;

/// <summary>
/// Composition-root tests: Production refuses to start without a usable key-ring directory, Development and
/// Testing start without local configuration, and a configured directory outside the repository is the one the
/// protector actually uses (FR-PRV-04, NFR-SEC-05, ADR-0007 decision 5).
/// </summary>
public sealed class DataProtectionRegistrationTests : IDisposable
{
    private readonly List<string> _contentRoots = [];

    [Fact]
    public async Task Startup_Fails_In_Production_Without_A_KeysPath()
    {
        using var host = BuildHost(Environments.Production, keysPath: string.Empty);

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task Startup_Fails_In_Production_When_The_KeysPath_Is_Inside_The_Repository()
    {
        using var repository = new TemporaryRepository();
        using var host = BuildHost(
            Environments.Production,
            repository.InsideRepositoryPath,
            repository.ContentRoot);

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task Startup_Succeeds_In_Development_Without_A_KeysPath()
    {
        using var host = BuildHost(Environments.Development, keysPath: string.Empty);

        await host.StartAsync();
        try
        {
            var options = host.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value;

            Assert.Equal(string.Empty, options.KeysPath);
            Assert.False(
                DataProtectionKeyPath.IsInsideRepository(
                    DataProtectionKeyPath.DefaultDevelopmentPath,
                    host.Services.GetRequiredService<IHostEnvironment>().ContentRootPath));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Configured_Path_Outside_The_Repository_Is_Used_By_The_Protector()
    {
        using var repository = new TemporaryRepository();
        using var host = BuildHost(
            Environments.Production,
            repository.OutsideRepositoryPath,
            repository.ContentRoot);

        await host.StartAsync();
        try
        {
            var protector = host.Services.GetRequiredService<IDiagnosisProtector>();

            var ciphertext = protector.Protect("Community-acquired pneumonia");

            Assert.Equal("Community-acquired pneumonia", protector.Unprotect(ciphertext));
            Assert.NotEmpty(Directory.GetFiles(repository.OutsideRepositoryPath, "*.xml"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private IHost BuildHost(string environmentName, string keysPath, string? contentRootPath = null)
    {
        if (contentRootPath is null)
        {
            contentRootPath = Path.Combine(
                Path.GetTempPath(),
                "MediPassPlus.Tests",
                "host",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(contentRootPath);
            _contentRoots.Add(contentRootPath);
        }

        return new HostBuilder()
            .UseEnvironment(environmentName)
            .UseContentRoot(contentRootPath)
            .ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        [$"{DataProtectionOptions.SectionName}:{nameof(DataProtectionOptions.KeysPath)}"] = keysPath
                    }))
            .ConfigureServices((context, services) =>
                services.AddDiagnosisProtection(context.Configuration, context.HostingEnvironment))
            .Build();
    }

    public void Dispose()
    {
        foreach (var contentRoot in _contentRoots)
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }
}

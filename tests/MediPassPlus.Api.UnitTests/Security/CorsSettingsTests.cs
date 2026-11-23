using System.ComponentModel.DataAnnotations;
using MediPassPlus.Api.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MediPassPlus.Api.UnitTests.Security;

public class CorsSettingsTests
{
    [Fact]
    public void Defaults_AreRejectedBecauseNoOriginIsConfigured()
    {
        // There is no committed localhost fallback: a host that forgot its Cors section must fail startup rather than
        // answer a developer origin (NFR-SEC-07).
        Assert.False(IsValid(new CorsSettings()));
    }

    [Fact]
    public void AMissingSection_BindsToAnEmptyListAndFailsValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var services = new ServiceCollection();
        services.AddOptions<CorsSettings>()
            .Bind(configuration.GetSection(CorsSettings.SectionName))
            .ValidateDataAnnotations();

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<CorsSettings>>().Value);

        Assert.Contains(
            exception.Failures,
            failure => failure.Contains("must list at least one origin", StringComparison.Ordinal));
    }

    [Fact]
    public void AnHttpsOrigin_IsValid()
    {
        var settings = new CorsSettings { AllowedOrigins = ["https://medipass.example"] };

        Assert.True(IsValid(settings));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("localhost:4200")]
    [InlineData("http://localhost:4200/")]
    [InlineData("http://localhost:4200/api")]
    [InlineData("ftp://localhost:4200")]
    [InlineData("https://user@localhost:4200")]
    public void AValueThatIsNotAnHttpOrigin_IsRejected(string origin)
    {
        var settings = new CorsSettings { AllowedOrigins = [origin] };

        Assert.False(IsValid(settings));
    }

    [Fact]
    public void AnEmptyList_IsRejected()
    {
        var settings = new CorsSettings { AllowedOrigins = [] };

        Assert.False(IsValid(settings));
    }

    private static bool IsValid(CorsSettings settings) =>
        Validator.TryValidateObject(
            settings,
            new ValidationContext(settings),
            new List<ValidationResult>(),
            validateAllProperties: true);
}

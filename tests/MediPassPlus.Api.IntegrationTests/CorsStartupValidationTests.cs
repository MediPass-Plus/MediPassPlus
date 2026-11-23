using MediPassPlus.Api.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// Proves the CORS list is really validated at host startup (NFR-SEC-07). <c>CorsSettingsTests</c> proves the validator
/// rejects a bad list; these tests boot the real host with a bad <c>Cors:AllowedOrigins</c> and assert that startup
/// fails, so removing <c>ValidateOnStart()</c> from <c>Program.cs</c> cannot pass unnoticed.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class CorsStartupValidationTests
{
    [Fact]
    public async Task AnEmptyOriginList_StopsHostStartup()
    {
        await AssertStartupFailsAsync(
            settings => settings.AllowedOrigins = [],
            "Cors:AllowedOrigins must list at least one origin.");
    }

    [Fact]
    public async Task AWildcardOrigin_StopsHostStartup()
    {
        await AssertStartupFailsAsync(
            settings => settings.AllowedOrigins = ["*"],
            "must be an absolute http or https origin without a path");
    }

    private static async Task AssertStartupFailsAsync(
        Action<CorsSettings> configure,
        string expectedFailure)
    {
        // startHost: false keeps the instance so the test can dispose the database and the environment variables it
        // owns after asserting the failure.
        await using var factory = new MediPassApiFactory(
            nameof(CorsStartupValidationTests),
            "Testing",
            exposeApiReference: true,
            configureTestServices: services => services.Configure(configure),
            startHost: false);

        var exception = Record.Exception(factory.EnsureHostStarted);

        var validation = FindOptionsValidationException(exception);
        Assert.NotNull(validation);
        Assert.Contains(validation.Failures, failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
    }

    private static OptionsValidationException? FindOptionsValidationException(Exception? exception)
    {
        while (exception is not null)
        {
            if (exception is OptionsValidationException validation)
            {
                return validation;
            }

            exception = exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1
                ? aggregate.InnerExceptions[0]
                : exception.InnerException;
        }

        return null;
    }
}

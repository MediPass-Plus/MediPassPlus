using MediPassPlus.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MediPassPlus.Infrastructure.Tests.Email;

/// <summary>
/// Configuration tests for the password-reset SMTP transport. An empty host selects the no-op sender in Development
/// and Testing only; every other environment must configure a transport. A host with no credentials, an unusable
/// account or sender address, or a timeout outside the supported range must stop the host at startup instead of
/// surfacing as a <c>500</c> on the first reset request (FR-ACC-11, NFR-SEC-06, FR-PRV-08).
/// </summary>
public sealed class SmtpSettingsValidatorTests
{
    private const string Password = "abcdefghijklmnop";

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void No_Host_Is_Accepted_In_Development_And_Testing(string environment)
    {
        var result = Validate(new SmtpSettings(), environment);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void No_Host_Is_Refused_Outside_Development_And_Testing(string environment)
    {
        var result = Validate(new SmtpSettings(), environment);

        Assert.True(result.Failed);
        Assert.Contains("Smtp:Host", result.FailureMessage);
    }

    [Fact]
    public async Task Startup_Fails_In_Production_Without_A_Host()
    {
        using var host = BuildHost(Environments.Production, smtpHost: string.Empty);

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task Startup_Succeeds_In_Development_Without_A_Host()
    {
        using var host = BuildHost(Environments.Development, smtpHost: string.Empty);

        await host.StartAsync();

        try
        {
            Assert.Equal(
                string.Empty,
                host.Services.GetRequiredService<IOptions<SmtpSettings>>().Value.Host);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public void A_Complete_Configuration_Is_Accepted()
    {
        var result = Validate(Complete());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Missing_UserName_Is_Refused(string userName)
    {
        var settings = Complete();
        settings.UserName = userName;

        var result = Validate(settings);

        Assert.True(result.Failed);
        Assert.Contains("Smtp:UserName", result.FailureMessage);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("MediPass+ <medipass.notifications@example.test>")]
    [InlineData("first@medipass.test, second@medipass.test")]
    [InlineData("first@medipass.test;second@medipass.test")]
    public void An_Unusable_UserName_Is_Refused(string userName)
    {
        var settings = Complete();
        settings.UserName = userName;

        var result = Validate(settings);

        Assert.True(result.Failed);
        Assert.Contains("Smtp:UserName", result.FailureMessage);
    }

    [Fact]
    public void A_Missing_Password_Is_Refused()
    {
        var settings = Complete();
        settings.Password = "   ";

        var result = Validate(settings);

        Assert.True(result.Failed);
        Assert.Contains("Smtp:Password", result.FailureMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void A_Port_Outside_The_Tcp_Range_Is_Refused(int port)
    {
        var settings = Complete();
        settings.Port = port;

        var result = Validate(settings);

        Assert.True(result.Failed);
        Assert.Contains("Smtp:Port", result.FailureMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(121)]
    public void A_Timeout_Outside_The_Supported_Range_Is_Refused(int timeoutSeconds)
    {
        var settings = Complete();
        settings.TimeoutSeconds = timeoutSeconds;

        var result = Validate(settings);

        Assert.True(result.Failed);
        Assert.Contains("Smtp:TimeoutSeconds", result.FailureMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("MediPass+ <medipass.notifications@gmail.com>")]
    [InlineData("first@medipass.test, second@medipass.test")]
    [InlineData("first@medipass.test;second@medipass.test")]
    public void An_Unusable_FromAddress_Is_Refused(string fromAddress)
    {
        var settings = Complete();
        settings.FromAddress = fromAddress;

        var result = Validate(settings);

        Assert.True(result.Failed);
        Assert.Contains("Smtp:FromAddress", result.FailureMessage);
    }

    [Fact]
    public void FromAddress_Has_No_Placeholder_Default()
    {
        Assert.Equal(string.Empty, new SmtpSettings().FromAddress);
    }

    [Fact]
    public void Every_Broken_Setting_Is_Reported_Together()
    {
        var settings = new SmtpSettings
        {
            Host = "smtp.example.test",
            Port = 0,
            TimeoutSeconds = 0,
            UserName = string.Empty,
            Password = string.Empty,
            FromAddress = string.Empty
        };

        var result = Validate(settings);

        Assert.True(result.Failed);
        Assert.Contains("Smtp:Port", result.FailureMessage);
        Assert.Contains("Smtp:TimeoutSeconds", result.FailureMessage);
        Assert.Contains("Smtp:UserName", result.FailureMessage);
        Assert.Contains("Smtp:Password", result.FailureMessage);
        Assert.Contains("Smtp:FromAddress", result.FailureMessage);
    }

    [Fact]
    public void A_Refusal_Never_Repeats_The_Password()
    {
        var settings = Complete();
        settings.Password = Password;
        settings.FromAddress = "not-an-address";

        var result = Validate(settings);

        Assert.True(result.Failed);
        Assert.DoesNotContain(Password, result.FailureMessage, StringComparison.Ordinal);
    }

    private static ValidateOptionsResult Validate(SmtpSettings settings, string environment = "Testing") =>
        new SmtpSettingsValidator(new TestEnvironment(environment)).Validate(Options.DefaultName, settings);

    private static IHost BuildHost(string environment, string smtpHost) =>
        new HostBuilder()
            .UseEnvironment(environment)
            .ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        [$"{SmtpSettings.SectionName}:Host"] = smtpHost
                    }))
            .ConfigureServices((context, services) =>
            {
                services.AddOptions<SmtpSettings>()
                    .Bind(context.Configuration.GetSection(SmtpSettings.SectionName))
                    .ValidateOnStart();
                services.AddSingleton<IValidateOptions<SmtpSettings>, SmtpSettingsValidator>();
            })
            .Build();

    private static SmtpSettings Complete() => new()
    {
        Host = "smtp.example.test",
        Port = 587,
        UseStartTls = true,
        UserName = "medipass.notifications@example.test",
        Password = Password,
        FromAddress = "medipass.notifications@example.test",
        FromName = "MediPass+",
        TimeoutSeconds = 10
    };

    private sealed class TestEnvironment : IHostEnvironment
    {
        public TestEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "MediPassPlus.Infrastructure.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

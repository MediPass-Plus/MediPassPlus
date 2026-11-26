using MediPassPlus.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace MediPassPlus.Infrastructure.Tests.Email;

/// <summary>
/// Configuration tests for the password-reset SMTP transport. An empty host selects the no-op sender on purpose, but a
/// host with no credentials or an unusable sender address must stop the host at startup instead of surfacing as a
/// <c>500</c> on the first reset request (FR-ACC-11, NFR-SEC-06, FR-PRV-08).
/// </summary>
public sealed class SmtpSettingsValidatorTests
{
    private const string Password = "abcdefghijklmnop";

    [Fact]
    public void No_Host_Is_Accepted_Because_The_NoOp_Sender_Is_Used()
    {
        var result = Validate(new SmtpSettings());

        Assert.True(result.Succeeded);
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
    public void Every_Broken_Setting_Is_Reported_Together()
    {
        var settings = new SmtpSettings
        {
            Host = "smtp.example.test",
            Port = 0,
            UserName = string.Empty,
            Password = string.Empty,
            FromAddress = string.Empty
        };

        var result = Validate(settings);

        Assert.True(result.Failed);
        Assert.Contains("Smtp:Port", result.FailureMessage);
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

    private static ValidateOptionsResult Validate(SmtpSettings settings) =>
        new SmtpSettingsValidator().Validate(Options.DefaultName, settings);

    private static SmtpSettings Complete() => new()
    {
        Host = "smtp.example.test",
        Port = 587,
        UseStartTls = true,
        UserName = "medipass.notifications@example.test",
        Password = Password,
        FromAddress = "medipass.notifications@example.test",
        FromName = "MediPass+"
    };
}

using System.ComponentModel.DataAnnotations;
using MediPassPlus.Api.Security;

namespace MediPassPlus.Api.UnitTests.Security;

public class ForwardedHeadersSettingsTests
{
    [Fact]
    public void DisabledWithoutProxies_IsValid()
    {
        Assert.True(IsValid(new ForwardedHeadersSettings()));
    }

    [Fact]
    public void EnabledWithoutProxies_IsRejected()
    {
        var settings = new ForwardedHeadersSettings { Enabled = true };

        Assert.False(IsValid(settings));
    }

    [Fact]
    public void EnabledWithTrustAllProxies_IsValid()
    {
        var settings = new ForwardedHeadersSettings
        {
            Enabled = true,
            TrustAllProxies = true
        };

        Assert.True(IsValid(settings));
    }

    [Fact]
    public void EnabledWithAKnownProxy_IsValid()
    {
        var settings = new ForwardedHeadersSettings
        {
            Enabled = true,
            KnownProxies = ["10.0.0.5"]
        };

        Assert.True(IsValid(settings));
    }

    [Fact]
    public void EnabledWithAValueThatIsNotAnIpAddress_IsRejected()
    {
        var settings = new ForwardedHeadersSettings
        {
            Enabled = true,
            KnownProxies = ["proxy.internal"]
        };

        Assert.False(IsValid(settings));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void AForwardLimitOutsideTheRange_IsRejected(int forwardLimit)
    {
        var settings = new ForwardedHeadersSettings
        {
            Enabled = true,
            TrustAllProxies = true,
            ForwardLimit = forwardLimit
        };

        Assert.False(IsValid(settings));
    }

    private static bool IsValid(ForwardedHeadersSettings settings) =>
        Validator.TryValidateObject(
            settings,
            new ValidationContext(settings),
            new List<ValidationResult>(),
            validateAllProperties: true);
}

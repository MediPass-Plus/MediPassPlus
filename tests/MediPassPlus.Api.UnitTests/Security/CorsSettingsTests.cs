using System.ComponentModel.DataAnnotations;
using MediPassPlus.Api.Security;

namespace MediPassPlus.Api.UnitTests.Security;

public class CorsSettingsTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        Assert.True(IsValid(new CorsSettings()));
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

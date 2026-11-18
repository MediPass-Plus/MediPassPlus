using MediPassPlus.Application.Dtos.Profile;
using MediPassPlus.Application.Validator;

namespace MediPassPlus.Application.Tests.Validator;

public class UpdateProfileRequestValidatorTests
{
    private readonly UpdateProfileRequestValidator _validator = new();

    [Theory]
    [InlineData("Ahmed Ali")]
    [InlineData("  اسم عربي  ")]
    public void Valid_name_passes(string fullName)
    {
        Assert.True(_validator.Validate(new UpdateProfileRequest(fullName, null)).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_name_is_reported(string fullName)
    {
        var result = _validator.Validate(new UpdateProfileRequest(fullName, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(UpdateProfileRequest.FullName));
    }

    [Fact]
    public void Name_longer_than_100_characters_is_reported()
    {
        var result = _validator.Validate(new UpdateProfileRequest(new string('a', 101), null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(UpdateProfileRequest.FullName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_phone_is_allowed_and_means_clear(string? phoneNumber)
    {
        Assert.True(_validator.Validate(new UpdateProfileRequest("Ahmed Ali", phoneNumber)).IsValid);
    }

    [Theory]
    [InlineData("01012345678")]
    [InlineData("+201012345678")]
    [InlineData("1234567")]
    [InlineData(" +201012345678 ")]
    public void Dialable_phone_passes(string phoneNumber)
    {
        Assert.True(_validator.Validate(new UpdateProfileRequest("Ahmed Ali", phoneNumber)).IsValid);
    }

    [Theory]
    [InlineData("010123")]           // too short
    [InlineData("0101234567890123")] // too long
    [InlineData("010-123-4567")]     // separators are not dialable
    [InlineData("phone")]
    [InlineData("+20 101 234 5678")]
    public void Invalid_phone_is_reported(string phoneNumber)
    {
        var result = _validator.Validate(new UpdateProfileRequest("Ahmed Ali", phoneNumber));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(UpdateProfileRequest.PhoneNumber));
    }
}

using MediPassPlus.Application.Dtos.Auth;
using MediPassPlus.Application.Validator;

namespace MediPassPlus.Application.Tests.Validator;

public class ForgotPasswordDtoValidatorTests
{
    private readonly ForgotPasswordDtoValidator _validator = new();

    [Theory]
    [InlineData("demo@test.com")]
    [InlineData("user.name+tag@sub.domain.co")]
    public void Valid_email_passes(string email)
    {
        Assert.True(_validator.Validate(new ForgotPasswordDto(email)).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("a@")]
    public void Invalid_email_is_reported(string email)
    {
        var result = _validator.Validate(new ForgotPasswordDto(email));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(ForgotPasswordDto.Email));
    }
}

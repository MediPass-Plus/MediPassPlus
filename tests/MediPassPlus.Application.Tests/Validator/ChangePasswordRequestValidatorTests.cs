using MediPassPlus.Application.Dtos.Profile;
using MediPassPlus.Application.Validator;

namespace MediPassPlus.Application.Tests.Validator;

public class ChangePasswordRequestValidatorTests
{
    private const string CurrentPassword = "Str0ng!Pass1";
    private const string CompliantNewPassword = "Str0ng!Pass2";

    private readonly ChangePasswordRequestValidator _validator = new();

    [Fact]
    public void Compliant_change_passes()
    {
        Assert.True(_validator.Validate(new ChangePasswordRequest(CurrentPassword, CompliantNewPassword)).IsValid);
    }

    [Fact]
    public void Empty_current_password_is_reported()
    {
        var result = _validator.Validate(new ChangePasswordRequest(string.Empty, CompliantNewPassword));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(ChangePasswordRequest.CurrentPassword));
    }

    [Theory]
    [InlineData("")]              // empty
    [InlineData("Sh0rt!")]        // under 8 characters
    [InlineData("str0ng!pass")]   // no uppercase letter
    [InlineData("STR0NG!PASS")]   // no lowercase letter
    [InlineData("Strong!Pass")]   // no digit
    [InlineData("Str0ngPass1")]   // no special character
    public void Weak_new_password_is_reported(string newPassword)
    {
        var result = _validator.Validate(new ChangePasswordRequest(CurrentPassword, newPassword));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(ChangePasswordRequest.NewPassword));
    }

    [Fact]
    public void New_password_equal_to_the_current_one_is_reported()
    {
        var result = _validator.Validate(new ChangePasswordRequest(CurrentPassword, CurrentPassword));

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == nameof(ChangePasswordRequest.NewPassword));
    }
}

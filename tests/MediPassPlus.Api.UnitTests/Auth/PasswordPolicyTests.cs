using FluentValidation.Results;
using MediPassPlus.Application.Dtos.AdminManagement;
using MediPassPlus.Application.Dtos.Auth;
using MediPassPlus.Application.Validator;

namespace MediPassPlus.Api.UnitTests.Auth;

/// <summary>
/// Guards the password rules the application actually enforces (FR-ACC-02).
///
/// Identity has always required a lowercase letter, but the DTO validators did not check it, so a
/// password such as <c>AHMED1234!</c> passed validation and then failed inside
/// <c>UserManager.CreateAsync</c> with a second, later error. Every password validator must now
/// reject the same passwords Identity rejects, and accept the ones it accepts.
/// </summary>
public sealed class PasswordPolicyTests
{
    /// <summary>
    /// Password cases for the registration validator. The policy is the stricter one the team kept:
    /// 8 characters with a lowercase letter, an uppercase letter, a digit and a special character.
    /// </summary>
    /// <returns>The password, whether it is valid and a fragment of the expected error message.</returns>
    public static IEnumerable<object[]> RegisterPasswordCases()
    {
        yield return ["Str0ng!Pass", true, string.Empty];
        yield return ["Abcdef1!", true, string.Empty];

        // The bug this suite was written for: Identity rejected this, validation did not.
        yield return ["AHMED1234!", false, "lowercase"];

        yield return ["ahmed1234!", false, "uppercase"];
        yield return ["Ahmed!@#$", false, "digit"];
        yield return ["Ahmed1234", false, "special character"];
        yield return ["Ah1!a", false, "at least 8"];
    }

    /// <summary>Registration accepts the compliant passwords and rejects every incomplete one.</summary>
    /// <param name="password">The password under test.</param>
    /// <param name="isValid">Whether the validator is expected to accept it.</param>
    /// <param name="expectedError">A fragment of the expected error message when it is rejected.</param>
    [Theory]
    [MemberData(nameof(RegisterPasswordCases))]
    public void Register_validator_enforces_the_password_policy(
        string password, bool isValid, string expectedError)
    {
        var result = new RegisterDtoValidator().Validate(
            new RegisterDto("Test Patient", "patient@example.test", password, "Patient"));

        Assert.Equal(isValid, result.IsValid);

        if (!isValid)
        {
            Assert.Contains(
                result.Errors,
                failure => failure.ErrorMessage.Contains(expectedError, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// All four password validators reject a password without a lowercase letter. They are separate
    /// copies of the same rules, so a fix applied to one of them can silently miss the others.
    /// </summary>
    [Fact]
    public void Every_password_validator_rejects_a_password_without_a_lowercase_letter()
    {
        const string password = "AHMED1234!";

        (string Validator, ValidationResult Result)[] results =
        [
            ("RegisterDtoValidator", new RegisterDtoValidator().Validate(
                new RegisterDto("Test Patient", "patient@example.test", password, "Patient"))),
            ("ResetPasswordDtoValidator", new ResetPasswordDtoValidator().Validate(
                new ResetPasswordDto("patient@example.test", "reset-token", password))),
            ("CreateDoctorDtoValidator", new CreateDoctorDtoValidator().Validate(
                new CreateDoctorDto("Test Doctor", "doctor@example.test", password, "Cardiology", 1))),
            ("CreateStaffDtoValidator", new CreateStaffDtoValidator().Validate(
                new CreateStaffDto("Test Staff", "staff@example.test", password, 1)))
        ];

        foreach (var (validator, result) in results)
        {
            Assert.False(result.IsValid, $"{validator} must reject '{password}'.");
            Assert.Contains(
                result.Errors,
                failure => failure.ErrorMessage.Contains("lowercase", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>A compliant password is accepted by every validator, so the rules did not drift apart.</summary>
    [Fact]
    public void Every_password_validator_accepts_a_compliant_password()
    {
        const string password = "Str0ng!Pass";

        Assert.True(new RegisterDtoValidator().Validate(
            new RegisterDto("Test Patient", "patient@example.test", password, "Patient")).IsValid);
        Assert.True(new ResetPasswordDtoValidator().Validate(
            new ResetPasswordDto("patient@example.test", "reset-token", password)).IsValid);
        Assert.True(new CreateDoctorDtoValidator().Validate(
            new CreateDoctorDto("Test Doctor", "doctor@example.test", password, "Cardiology", 1)).IsValid);
        Assert.True(new CreateStaffDtoValidator().Validate(
            new CreateStaffDto("Test Staff", "staff@example.test", password, 1)).IsValid);
    }
}

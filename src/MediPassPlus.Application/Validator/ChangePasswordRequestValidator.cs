using FluentValidation;
using MediPassPlus.Application.Dtos.Profile;

namespace MediPassPlus.Application.Validator;

/// <summary>
/// The account password policy, copied from <c>RegisterDtoValidator</c> so a new password cannot pass here and
/// then fail inside Identity with a different message (FR-ACC-02, FR-ACC-08).
/// </summary>
public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    /// <summary>Initializes the validator with the password-policy rules.</summary>
    public ChangePasswordRequestValidator()
    {
        RuleFor(request => request.CurrentPassword)
            .NotEmpty();

        RuleFor(request => request.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter")
            .Matches("[0-9]").WithMessage("Password must contain a digit")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain a special character");

        RuleFor(request => request.NewPassword)
            .NotEqual(request => request.CurrentPassword)
            .WithMessage("The new password must be different from the current password.");
    }
}

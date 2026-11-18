using System.Text.RegularExpressions;
using FluentValidation;
using MediPassPlus.Application.Dtos.Profile;

namespace MediPassPlus.Application.Validator;

/// <summary>
/// Rules for the self-service profile update. The name rules mirror <c>RegisterDtoValidator</c>, so a value that
/// passes here cannot be rejected later by Identity (FR-ACC-07, US-03).
/// </summary>
public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    private static readonly Regex PhoneNumberPattern = new(@"^\+?[0-9]{7,15}$", RegexOptions.Compiled);

    /// <summary>Initializes the validator with the name and phone rules.</summary>
    public UpdateProfileRequestValidator()
    {
        RuleFor(request => request.FullName)
            .NotEmpty()
            .MaximumLength(100);

        // An empty phone clears the stored value; a present one must look like a dialable number. Surrounding
        // whitespace is ignored here and trimmed by the endpoint, because a pasted number usually carries it.
        RuleFor(request => request.PhoneNumber)
            .MaximumLength(20)
            .Must(value => value is not null && PhoneNumberPattern.IsMatch(value.Trim()))
            .WithMessage("Phone number must contain 7 to 15 digits and may start with '+'.")
            .When(request => !string.IsNullOrWhiteSpace(request.PhoneNumber));
    }
}

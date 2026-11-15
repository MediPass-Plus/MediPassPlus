using FluentValidation;
using MediPassPlus.Application.Dtos.Auth;

namespace MediPassPlus.Application.Validator;

/// <summary>
/// Guards the refresh and logout body. The token is opaque, so only its presence and a sane upper bound are
/// checked; a value that does not resolve fails as an unknown token, not as a validation error.
/// </summary>
public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(request => request.RefreshToken)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(request => request.DeviceId)
            .MaximumLength(128)
            .When(request => request.DeviceId is not null);
    }
}

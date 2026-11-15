using System;
using System.Collections.Generic;
using System.Text;
using MediPassPlus.Application.Dtos.Auth;
using FluentValidation;

namespace MediPassPlus.Application.Validator;

public class LoginDtoValidator : AbstractValidator<LoginDto>
{
    public LoginDtoValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
        RuleFor(x => x.DeviceId).MaximumLength(128).When(x => x.DeviceId is not null);
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using FluentValidation;
using MediPassPlus.Application.Dtos.Auth;

namespace MediPassPlus.Application.Validator;

public class ForgotPasswordDtoValidator : AbstractValidator<ForgotPasswordDto>
{
    public ForgotPasswordDtoValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
    }
}

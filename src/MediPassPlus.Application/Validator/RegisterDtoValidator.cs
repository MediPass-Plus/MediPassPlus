using System;
using System.Collections.Generic;
using System.Text;
using FluentValidation;
using MediPassPlus.Application.Dtos.Auth;
using Microsoft.AspNetCore.Identity;

namespace MediPassPlus.Application.Validator;

public  class RegisterDtoValidator : AbstractValidator<RegisterDto>
{
    public RegisterDtoValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password)
     .NotEmpty()
     .MinimumLength(8)
     .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter")
     .Matches("[0-9]").WithMessage("Password must contain a digit")
     .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain a special character");
        RuleFor(x => x.Role).NotEmpty();
    }
}

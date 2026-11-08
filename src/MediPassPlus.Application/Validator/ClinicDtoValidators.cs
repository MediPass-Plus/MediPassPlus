using FluentValidation;
using MediPassPlus.Application.Dtos.AdminManagement;

namespace MediPassPlus.Application.Validator;

public class CreateClinicDtoValidator : AbstractValidator<CreateClinicDto>
{
    public CreateClinicDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Phone).MaximumLength(30);
    }
}

public class UpdateClinicDtoValidator : AbstractValidator<UpdateClinicDto>
{
    public UpdateClinicDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Phone).MaximumLength(30);
    }
}
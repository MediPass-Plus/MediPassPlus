using FluentValidation;
using MediPassPlus.Application.Staff;

namespace MediPassPlus.Application.Validator;

public class CreateStaffSlotDtoValidator : AbstractValidator<CreateStaffSlotDto>
{
    public CreateStaffSlotDtoValidator()
    {
        RuleFor(x => x.StartTimeUtc).NotEmpty();
        RuleFor(x => x.EndTimeUtc).NotEmpty().GreaterThan(x => x.StartTimeUtc)
            .WithMessage("End time must be after start time.");
    }
}
using FluentValidation;
using MediPassPlus.Application.Staff;

namespace MediPassPlus.Application.Validator;

public class StaffBookAppointmentDtoValidator : AbstractValidator<StaffBookAppointmentDto>
{
    public StaffBookAppointmentDtoValidator()
    {
        RuleFor(x => x.PatientEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.DoctorId).GreaterThan(0);
        RuleFor(x => x.SlotId).GreaterThan(0);
    }
}
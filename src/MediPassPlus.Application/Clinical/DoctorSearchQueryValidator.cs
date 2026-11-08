using FluentValidation;

namespace MediPassPlus.Application.Clinical;

public sealed class DoctorSearchQueryValidator : AbstractValidator<DoctorSearchQuery>
{
    public DoctorSearchQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).GreaterThanOrEqualTo(1);
        RuleFor(q => q.Search).MaximumLength(100);
        RuleFor(q => q.Specialty).MaximumLength(100);
    }
}


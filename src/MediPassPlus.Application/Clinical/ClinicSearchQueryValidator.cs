using FluentValidation;

namespace MediPassPlus.Application.Clinical;

public sealed class ClinicSearchQueryValidator : AbstractValidator<ClinicSearchQuery>
{
    public ClinicSearchQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).GreaterThanOrEqualTo(1);
        RuleFor(q => q.Search).MaximumLength(100);
    }
}


using FluentValidation;
using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.Clinical;

public sealed class ClinicSearchQueryValidator : AbstractValidator<ClinicSearchQuery>
{
    public ClinicSearchQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
        RuleFor(q => q.Search).MaximumLength(100);
    }
}


using FluentValidation;

namespace MediPassPlus.Application.Clinical;

public sealed class SlotSearchQueryValidator : AbstractValidator<SlotSearchQuery>
{
    public SlotSearchQueryValidator()
    {
        RuleFor(q => q.To)
            .Must((q, to) => to!.Value >= q.From!.Value)
            .When(q => q.From.HasValue && q.To.HasValue)
            .WithMessage("'To' must not be before 'From'.");
    }
}

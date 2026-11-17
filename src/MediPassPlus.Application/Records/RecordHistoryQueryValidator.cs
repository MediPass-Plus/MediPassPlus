using FluentValidation;
using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.Records;

/// <summary>
/// Validates the paging values of both history endpoints. A page size above 50 is refused with <c>400</c> instead of
/// being silently trusted (NFR-PRF-04, NFR-SEC-06). The two pages are validated independently because the caller can
/// page the reports separately from the diagnoses.
/// </summary>
public sealed class RecordHistoryQueryValidator : AbstractValidator<RecordHistoryQuery>
{
    /// <summary>Initializes the paging rules for the history endpoints.</summary>
    public RecordHistoryQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
        RuleFor(query => query.ReportPage).GreaterThanOrEqualTo(1);
        RuleFor(query => query.ReportPageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
    }
}

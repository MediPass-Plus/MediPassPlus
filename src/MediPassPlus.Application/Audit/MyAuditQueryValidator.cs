using FluentValidation;
using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.Audit;

/// <summary>
/// Validates the paging values of the patient's audit list. A page size above 50 is refused with <c>400</c> instead of
/// being silently trusted (NFR-PRF-04, NFR-SEC-06).
/// </summary>
public sealed class MyAuditQueryValidator : AbstractValidator<MyAuditQuery>
{
    /// <summary>Initializes the paging rules for the audit list.</summary>
    public MyAuditQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
    }
}

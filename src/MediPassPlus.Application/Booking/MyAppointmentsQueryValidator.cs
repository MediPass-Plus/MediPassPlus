using FluentValidation;
using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.Booking;

/// <summary>
/// Validates the paging values of the patient's appointment list. The defaults are 1 and 20, and a page size above
/// 50 is refused with <c>400</c> instead of being silently trusted (NFR-PRF-04, NFR-SEC-06).
/// </summary>
public sealed class MyAppointmentsQueryValidator : AbstractValidator<MyAppointmentsQuery>
{
    /// <summary>Initializes the paging rules for the appointment list.</summary>
    public MyAppointmentsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
    }
}

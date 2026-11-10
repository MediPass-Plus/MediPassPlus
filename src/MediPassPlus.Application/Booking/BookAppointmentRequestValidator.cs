using FluentValidation;

namespace MediPassPlus.Application.Booking;

/// <summary>Validates the booking request before the action runs (NFR-SEC-06).</summary>
public sealed class BookAppointmentRequestValidator : AbstractValidator<BookAppointmentRequest>
{
    /// <summary>Initializes the rules for a booking request.</summary>
    public BookAppointmentRequestValidator()
    {
        RuleFor(request => request.DoctorId).GreaterThan(0);
        RuleFor(request => request.SlotId).GreaterThan(0);
    }
}

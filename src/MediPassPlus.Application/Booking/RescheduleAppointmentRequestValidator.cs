using FluentValidation;

namespace MediPassPlus.Application.Booking;

/// <summary>Validates the reschedule request before the action runs (NFR-SEC-06).</summary>
public sealed class RescheduleAppointmentRequestValidator : AbstractValidator<RescheduleAppointmentRequest>
{
    /// <summary>Initializes the rules for a reschedule request.</summary>
    public RescheduleAppointmentRequestValidator()
    {
        RuleFor(request => request.NewSlotId).GreaterThan(0);
    }
}

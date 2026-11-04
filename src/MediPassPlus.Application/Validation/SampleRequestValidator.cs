using FluentValidation;

namespace MediPassPlus.Application.Validation;

/// <summary>
/// Reference validator for <see cref="SampleRequest"/>. It is the worked example MP-1306 asks for: copy this shape
/// for every new request contract so validation stays consistent across modules (NFR-SEC-06).
/// </summary>
public sealed class SampleRequestValidator : AbstractValidator<SampleRequest>
{
    /// <summary>
    /// Creates the validator and declares the rules for <see cref="SampleRequest"/>.
    /// </summary>
    public SampleRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(request => request.Email)
            .NotEmpty()
            .EmailAddress();
    }
}

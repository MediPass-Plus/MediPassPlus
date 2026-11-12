using FluentValidation;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Validates the six-digit code shape before it reaches the verification service. A malformed code is a
/// <c>400</c> validation failure and never increments the credential's attempt counter (NFR-SEC-06).
/// </summary>
public sealed class VerifyAccessCodeRequestValidator : AbstractValidator<VerifyAccessCodeRequest>
{
    /// <summary>Creates the validator and declares the six-digit rule.</summary>
    public VerifyAccessCodeRequestValidator()
    {
        RuleFor(request => request.Code)
            .NotEmpty()
            .Matches("^[0-9]{6}$")
            .WithMessage("The code must be exactly six digits.");
    }
}

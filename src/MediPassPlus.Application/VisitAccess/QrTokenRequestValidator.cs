using FluentValidation;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Validates the QR-token shape before it reaches the scan or decision service. A malformed token is a
/// <c>400</c> validation failure and never reaches the token hash lookup (NFR-SEC-06).
/// </summary>
public sealed class QrTokenRequestValidator : AbstractValidator<QrTokenRequest>
{
    /// <summary>The maximum accepted token length; a real token is 22 Base64Url characters.</summary>
    public const int MaximumTokenLength = 256;

    /// <summary>Creates the validator and declares the token rules.</summary>
    public QrTokenRequestValidator()
    {
        RuleFor(request => request.Token)
            .NotEmpty()
            .WithMessage("The QR token is required.")
            .MaximumLength(MaximumTokenLength)
            .WithMessage($"The QR token must be at most {MaximumTokenLength} characters.");
    }
}

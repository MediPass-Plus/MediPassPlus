using FluentValidation;

namespace MediPassPlus.Application.Records;

/// <summary>
/// Validates the diagnosis request before the action runs and before any medical row is staged (NFR-SEC-06,
/// ADR-0010). The 4,000-character limit applies to the plaintext, before encryption (ADR-0005).
/// </summary>
public sealed class CreateDiagnosisRequestValidator : AbstractValidator<CreateDiagnosisRequest>
{
    /// <summary>The maximum plaintext length of a diagnosis description (ADR-0005).</summary>
    public const int DescriptionMaxLength = 4000;

    /// <summary>Initializes the diagnosis rules.</summary>
    public CreateDiagnosisRequestValidator()
    {
        RuleFor(request => request.Description)
            .NotEmpty()
            .MaximumLength(DescriptionMaxLength);
    }
}

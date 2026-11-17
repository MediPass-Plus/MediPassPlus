using FluentValidation;

namespace MediPassPlus.Application.Records;

/// <summary>
/// Validates the prescription request before the action runs and before any medical row is staged (NFR-SEC-06,
/// ADR-0010). Every limit applies to the plaintext, before encryption (ADR-0005).
/// </summary>
public sealed class CreatePrescriptionRequestValidator : AbstractValidator<CreatePrescriptionRequest>
{
    /// <summary>The maximum plaintext length of a medicine name (ADR-0005).</summary>
    public const int MedicineNameMaxLength = 200;

    /// <summary>The maximum plaintext length of a dosage (ADR-0005).</summary>
    public const int DosageMaxLength = 200;

    /// <summary>The maximum plaintext length of the optional notes (ADR-0005).</summary>
    public const int NotesMaxLength = 1000;

    /// <summary>Initializes the prescription rules.</summary>
    public CreatePrescriptionRequestValidator()
    {
        RuleFor(request => request.MedicineName)
            .NotEmpty()
            .MaximumLength(MedicineNameMaxLength);

        RuleFor(request => request.Dosage)
            .NotEmpty()
            .MaximumLength(DosageMaxLength);

        // Notes are optional; a blank value is accepted and stored as null, never as an empty ciphertext.
        RuleFor(request => request.Notes)
            .MaximumLength(NotesMaxLength);
    }
}

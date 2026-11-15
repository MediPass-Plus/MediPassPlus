using MediPassPlus.Application.Records;

namespace MediPassPlus.Application.Tests.Records;

public class CreatePrescriptionRequestValidatorTests
{
    private readonly CreatePrescriptionRequestValidator _validator = new();

    private static CreatePrescriptionRequest Valid() => new()
    {
        MedicineName = "Amoxicillin",
        Dosage = "500 mg twice a day"
    };

    [Fact]
    public void A_complete_request_without_notes_passes()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void A_medicine_name_at_the_limit_passes()
    {
        var request = Valid();
        request.MedicineName = new string('a', CreatePrescriptionRequestValidator.MedicineNameMaxLength);

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void A_medicine_name_above_the_limit_is_refused()
    {
        var request = Valid();
        request.MedicineName = new string('a', CreatePrescriptionRequestValidator.MedicineNameMaxLength + 1);

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_medicine_name_is_refused(string medicineName)
    {
        var request = Valid();
        request.MedicineName = medicineName;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == nameof(CreatePrescriptionRequest.MedicineName));
    }

    [Fact]
    public void A_dosage_above_the_limit_is_refused()
    {
        var request = Valid();
        request.Dosage = new string('a', CreatePrescriptionRequestValidator.DosageMaxLength + 1);

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_dosage_is_refused(string dosage)
    {
        var request = Valid();
        request.Dosage = dosage;

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == nameof(CreatePrescriptionRequest.Dosage));
    }

    [Fact]
    public void Notes_at_the_limit_pass()
    {
        var request = Valid();
        request.Notes = new string('a', CreatePrescriptionRequestValidator.NotesMaxLength);

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void Notes_above_the_limit_are_refused()
    {
        var request = Valid();
        request.Notes = new string('a', CreatePrescriptionRequestValidator.NotesMaxLength + 1);

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Optional_notes_may_be_missing_or_blank(string? notes)
    {
        var request = Valid();
        request.Notes = notes;

        Assert.True(_validator.Validate(request).IsValid);
    }
}

using MediPassPlus.Application.Records;

namespace MediPassPlus.Application.Tests.Records;

public class CreateDiagnosisRequestValidatorTests
{
    private readonly CreateDiagnosisRequestValidator _validator = new();

    [Fact]
    public void A_description_at_the_limit_passes()
    {
        var request = new CreateDiagnosisRequest
        {
            Description = new string('a', CreateDiagnosisRequestValidator.DescriptionMaxLength)
        };

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void A_description_above_the_limit_is_refused()
    {
        var request = new CreateDiagnosisRequest
        {
            Description = new string('a', CreateDiagnosisRequestValidator.DescriptionMaxLength + 1)
        };

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_description_is_refused(string description)
    {
        var result = _validator.Validate(new CreateDiagnosisRequest { Description = description });

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == nameof(CreateDiagnosisRequest.Description));
    }

    [Fact]
    public void A_missing_description_is_refused()
    {
        var result = _validator.Validate(new CreateDiagnosisRequest { Description = null! });

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == nameof(CreateDiagnosisRequest.Description));
    }
}

using MediPassPlus.Application.Validation;

namespace MediPassPlus.Application.Tests;

public class SampleRequestValidatorTests
{
    private readonly SampleRequestValidator _validator = new();

    [Fact]
    public void Valid_request_passes()
    {
        var result = _validator.Validate(new SampleRequest("Arwa", "arwa@example.com"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Missing_fields_are_reported()
    {
        var result = _validator.Validate(new SampleRequest(null, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SampleRequest.Name));
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SampleRequest.Email));
    }

    [Fact]
    public void Malformed_email_is_reported()
    {
        var result = _validator.Validate(new SampleRequest("Arwa", "not-an-email"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SampleRequest.Email));
    }
}

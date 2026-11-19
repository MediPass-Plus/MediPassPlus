using MediPassPlus.Application.Audit;

namespace MediPassPlus.Application.Tests.Audit;

public class MyAuditQueryValidatorTests
{
    private readonly MyAuditQueryValidator _validator = new();

    [Theory]
    [InlineData(1, 20)]
    [InlineData(1, 1)]
    [InlineData(3, 50)]
    public void A_valid_page_passes(int page, int pageSize)
    {
        var query = new MyAuditQuery { Page = page, PageSize = pageSize };

        Assert.True(_validator.Validate(query).IsValid);
    }

    [Fact]
    public void A_page_below_one_is_refused()
    {
        var result = _validator.Validate(new MyAuditQuery { Page = 0 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(MyAuditQuery.Page));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void A_page_size_outside_the_allowed_range_is_refused(int pageSize)
    {
        var result = _validator.Validate(new MyAuditQuery { PageSize = pageSize });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(MyAuditQuery.PageSize));
    }
}

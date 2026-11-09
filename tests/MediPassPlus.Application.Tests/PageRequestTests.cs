using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.Tests;

public class PageRequestTests
{
    [Fact]
    public void Defaults_are_page_1_and_size_20()
    {
        var request = new PageRequest();

        Assert.Equal(1, request.Page);
        Assert.Equal(20, request.PageSize);
        Assert.Equal(20, request.GetPageSize());
        Assert.Equal(0, request.GetSkip());
    }

    [Fact]
    public void Page_size_above_the_maximum_is_cut_down()
    {
        var request = new PageRequest { PageSize = 5000 };

        Assert.Equal(PageRequest.MaxPageSize, request.GetPageSize());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(20, 20)]
    [InlineData(50, 50)]
    public void Page_size_within_the_limit_is_kept(int requested, int expected)
    {
        var request = new PageRequest { PageSize = requested };

        Assert.Equal(expected, request.GetPageSize());
    }

    [Fact]
    public void Skip_is_the_rows_of_the_earlier_pages()
    {
        var request = new PageRequest { Page = 3, PageSize = 20 };

        Assert.Equal(40, request.GetSkip());
    }

    [Fact]
    public void Skip_uses_the_cut_down_page_size()
    {
        var request = new PageRequest { Page = 2, PageSize = 5000 };

        Assert.Equal(PageRequest.MaxPageSize, request.GetSkip());
    }

    [Fact]
    public void Skip_does_not_overflow_for_a_huge_page_number()
    {
        var request = new PageRequest { Page = int.MaxValue, PageSize = 50 };

        Assert.Equal(int.MaxValue, request.GetSkip());
    }
}
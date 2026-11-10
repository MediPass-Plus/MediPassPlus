using MediPassPlus.Application.Email;

namespace MediPassPlus.Application.Tests.Email;

public class PasswordResetMessageTests
{
    [Fact]
    public void Link_escapes_email_and_token()
    {
        var link = PasswordResetMessage.BuildLink(
            "http://localhost:4200",
            "demo@test.com",
            "abc+def/ghi==");

        Assert.Equal(
            "http://localhost:4200/reset-password?email=demo%40test.com&token=abc%2Bdef%2Fghi%3D%3D",
            link);
    }

    [Fact]
    public void Link_tolerates_a_trailing_slash_on_the_base_url()
    {
        var link = PasswordResetMessage.BuildLink("http://localhost:4200/", "a@b.co", "t");

        Assert.StartsWith("http://localhost:4200/reset-password?", link);
    }

    [Fact]
    public void Body_carries_the_link_and_the_token()
    {
        var body = PasswordResetMessage.BuildBody("http://localhost:4200/reset?token=t", "t");

        Assert.Contains("http://localhost:4200/reset?token=t", body);
        Assert.Contains("24 hours", body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Link_rejects_a_blank_email(string blank)
    {
        Assert.Throws<ArgumentException>(
            () => PasswordResetMessage.BuildLink("http://localhost:4200", blank, "t"));
    }
}

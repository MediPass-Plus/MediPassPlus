using System.Security.Claims;
using MediPassPlus.Api.Identity;
using Microsoft.AspNetCore.Http;

namespace MediPassPlus.Api.UnitTests;

/// <summary>
/// The claim-to-id translation that every controller relies on (FR-ACC-07, FR-ACC-08). The cases pin the two
/// behaviours that matter: an unparsable claim is <c>null</c>, never <see cref="Guid.Empty"/>, and a request with
/// no <see cref="HttpContext"/> at all does not throw.
/// </summary>
public class HttpContextCurrentUserTests
{
    private static HttpContextCurrentUser CreateCurrentUser(ClaimsPrincipal? principal)
    {
        var context = principal is null ? null : new DefaultHttpContext { User = principal };

        return new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = context });
    }

    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "Bearer"));

    [Fact]
    public void UserId_ReturnsTheParsedIdentifier()
    {
        var userId = Guid.NewGuid();
        var currentUser = CreateCurrentUser(CreatePrincipal(
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())));

        Assert.Equal(userId, currentUser.UserId);
    }

    [Fact]
    public void UserId_IsNull_WhenTheClaimIsMissing()
    {
        var currentUser = CreateCurrentUser(CreatePrincipal());

        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void UserId_IsNull_WhenTheClaimIsNotAGuid()
    {
        var currentUser = CreateCurrentUser(CreatePrincipal(
            new Claim(ClaimTypes.NameIdentifier, "not-a-guid")));

        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void UserId_IsNull_WhenTheClaimIsGuidEmpty()
    {
        var currentUser = CreateCurrentUser(CreatePrincipal(
            new Claim(ClaimTypes.NameIdentifier, Guid.Empty.ToString())));

        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void UserId_IsSafe_WithoutAnHttpContext()
    {
        var currentUser = CreateCurrentUser(principal: null);

        Assert.Null(currentUser.UserId);
    }
}

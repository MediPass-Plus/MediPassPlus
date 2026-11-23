using System.Text;
using MediPassPlus.Api.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace MediPassPlus.Api.UnitTests.RateLimiting;

public class RateLimitEmailKeyMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_CapturesTheEmailAndRewindsTheBody()
    {
        var context = CreateLoginContext("""{"email":"Patient@Example.com","password":"Str0ng!Pass1"}""");

        await CreateMiddleware().InvokeAsync(context);

        Assert.Equal("patient@example.com", context.Items[RateLimitEmailKey.ItemKey]);
        Assert.Equal(0, context.Request.Body.Position);
    }

    [Fact]
    public async Task InvokeAsync_ReadsTheEmailCaseInsensitively()
    {
        var context = CreateLoginContext("""{"EMAIL":"doctor@example.com"}""");

        await CreateMiddleware().InvokeAsync(context);

        Assert.Equal("doctor@example.com", context.Items[RateLimitEmailKey.ItemKey]);
    }

    [Fact]
    public async Task InvokeAsync_WithMalformedJson_LeavesTheItemUnsetAndRewinds()
    {
        var context = CreateLoginContext("""{"email":""");

        await CreateMiddleware().InvokeAsync(context);

        Assert.False(context.Items.ContainsKey(RateLimitEmailKey.ItemKey));
        Assert.Equal(0, context.Request.Body.Position);
    }

    [Fact]
    public async Task InvokeAsync_WithoutAnEmailProperty_LeavesTheItemUnset()
    {
        var context = CreateLoginContext("""{"password":"Str0ng!Pass1"}""");

        await CreateMiddleware().InvokeAsync(context);

        Assert.False(context.Items.ContainsKey(RateLimitEmailKey.ItemKey));
    }

    [Fact]
    public async Task InvokeAsync_OnThePasswordResetPolicies_CapturesTheEmail()
    {
        var context = CreateContext("""{"email":"patient@example.com"}""", PasswordResetRateLimitPolicy.RequestName);
        await CreateMiddleware().InvokeAsync(context);
        Assert.Equal("patient@example.com", context.Items[RateLimitEmailKey.ItemKey]);

        context = CreateContext("""{"Email":"patient@example.com","Token":"abc"}""", PasswordResetRateLimitPolicy.SubmitName);
        await CreateMiddleware().InvokeAsync(context);
        Assert.Equal("patient@example.com", context.Items[RateLimitEmailKey.ItemKey]);
    }

    [Fact]
    public async Task InvokeAsync_OnARateLimitPolicyThatKeysByPatient_DoesNotReadTheBody()
    {
        var context = CreateContext("""{"email":"patient@example.com"}""", QrApproveRateLimitPolicy.Name);

        await CreateMiddleware().InvokeAsync(context);

        Assert.False(context.Items.ContainsKey(RateLimitEmailKey.ItemKey));
        Assert.Equal(0, context.Request.Body.Position);
    }

    [Fact]
    public async Task InvokeAsync_WithADuplicateEmailProperty_KeepsTheLastOneLikeModelBinding()
    {
        // System.Text.Json keeps the last duplicate, so MVC binds the victim; the limiter must spend the victim's
        // budget, not the decoy's, or the limit is bypassed with one fresh decoy per request (FR-PRV-07).
        var context = CreateLoginContext(
            """{"email":"decoy@example.com","email":"victim@example.com","password":"Str0ng!Pass1"}""");

        await CreateMiddleware().InvokeAsync(context);

        Assert.Equal("victim@example.com", context.Items[RateLimitEmailKey.ItemKey]);
    }

    [Fact]
    public async Task InvokeAsync_WithTheDuplicateInTheOtherOrder_KeepsTheLastOne()
    {
        var context = CreateLoginContext(
            """{"email":"victim@example.com","email":"decoy@example.com","password":"Str0ng!Pass1"}""");

        await CreateMiddleware().InvokeAsync(context);

        Assert.Equal("decoy@example.com", context.Items[RateLimitEmailKey.ItemKey]);
    }

    [Fact]
    public async Task InvokeAsync_WithACaseVariantDuplicate_KeepsTheLastOne()
    {
        var context = CreateLoginContext(
            """{"email":"first@example.com","EMAIL":"Second@Example.com"}""");

        await CreateMiddleware().InvokeAsync(context);

        Assert.Equal("second@example.com", context.Items[RateLimitEmailKey.ItemKey]);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheLastDuplicateIsNotAString_LeavesTheItemUnset()
    {
        var context = CreateLoginContext("""{"email":"first@example.com","email":42}""");

        await CreateMiddleware().InvokeAsync(context);

        Assert.False(context.Items.ContainsKey(RateLimitEmailKey.ItemKey));
    }

    [Fact]
    public async Task InvokeAsync_WithAnOversizedDeclaredBody_DoesNotReadIt()
    {
        var context = CreateLoginContext(new string('x', RateLimitEmailKeyMiddleware.MaximumBodyBytes + 1));

        await CreateMiddleware().InvokeAsync(context);

        Assert.False(context.Items.ContainsKey(RateLimitEmailKey.ItemKey));
        Assert.Equal(0, context.Request.Body.Position);
    }

    private static RateLimitEmailKeyMiddleware CreateMiddleware() =>
        new(_ => Task.CompletedTask);

    private static DefaultHttpContext CreateLoginContext(string body) =>
        CreateContext(body, LoginRateLimitPolicy.Name);

    private static DefaultHttpContext CreateContext(string body, string policyName)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bytes.Length;
        context.Request.Body = new MemoryStream(bytes);
        context.SetEndpoint(new Endpoint(
            requestDelegate: null,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute(policyName)),
            displayName: policyName));

        return context;
    }
}

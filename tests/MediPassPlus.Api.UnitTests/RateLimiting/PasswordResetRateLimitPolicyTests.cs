using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading.RateLimiting;
using MediPassPlus.Api.Controllers;
using MediPassPlus.Api.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MediPassPlus.Api.UnitTests.RateLimiting;

public class PasswordResetRateLimitPolicyTests
{
    private const string TestIpAddress = "198.51.100.23";

    [Fact]
    public void CreateRequestPartition_KeysByFamilyAndNormalizedEmailAndClientAddress()
    {
        var context = CreateContext("Doctor@Example.com");

        var partition = PasswordResetRateLimitPolicy.CreateRequestPartition(
            context,
            new PasswordResetRequestRateLimitOptions());

        Assert.Equal($"reset-request:email:doctor@example.com:ip:{TestIpAddress}", partition.PartitionKey);
    }

    [Fact]
    public void CreateSubmitPartition_KeysByFamilyAndClientAddress()
    {
        var context = CreateContext();

        var partition = PasswordResetRateLimitPolicy.CreateSubmitPartition(
            context,
            new PasswordResetSubmitRateLimitOptions());

        Assert.Equal($"reset-submit:ip:{TestIpAddress}", partition.PartitionKey);
    }

    [Fact]
    public void ARequestAndASubmitDoNotShareAWindow()
    {
        using var requestLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => PasswordResetRateLimitPolicy.CreateRequestPartition(
                context,
                new PasswordResetRequestRateLimitOptions { PermitLimit = 1 }));
        using var submitLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => PasswordResetRateLimitPolicy.CreateSubmitPartition(
                context,
                new PasswordResetSubmitRateLimitOptions { PermitLimit = 1 }));

        var context = CreateContext("patient@example.com");

        Assert.True(requestLimiter.AttemptAcquire(context).IsAcquired);
        Assert.True(submitLimiter.AttemptAcquire(context).IsAcquired);
    }

    [Fact]
    public void RequestPartition_DoesNotShareAKeyBetweenEmails()
    {
        var first = PasswordResetRateLimitPolicy.CreateRequestPartition(
            CreateContext("first@example.com"),
            new PasswordResetRequestRateLimitOptions());
        var second = PasswordResetRateLimitPolicy.CreateRequestPartition(
            CreateContext("second@example.com"),
            new PasswordResetRequestRateLimitOptions());

        Assert.NotEqual(first.PartitionKey, second.PartitionKey);
    }

    [Fact]
    public void RequestPartition_FallsBackToTheClientAddressWhenNoEmailWasCaptured()
    {
        var partition = PasswordResetRateLimitPolicy.CreateRequestPartition(
            CreateContext(),
            new PasswordResetRequestRateLimitOptions());

        Assert.Equal($"reset-request:ip:{TestIpAddress}", partition.PartitionKey);
    }

    [Fact]
    public void Policies_AllowThePermitLimitAndRejectTheNextAttempt()
    {
        using var requestLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => PasswordResetRateLimitPolicy.CreateRequestPartition(
                context,
                new PasswordResetRequestRateLimitOptions { PermitLimit = 2, WindowMinutes = 15 }));
        var context = CreateContext("patient@example.com");

        using var first = requestLimiter.AttemptAcquire(context);
        using var second = requestLimiter.AttemptAcquire(context);
        using var third = requestLimiter.AttemptAcquire(context);

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.False(third.IsAcquired);
        Assert.True(third.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task WriteRequestRejection_Writes429WithRetryAfterAndProblemDetails()
    {
        var (httpContext, body) = CreateResponseContext();

        await PasswordResetRateLimitPolicy.WriteRequestRejectionAsync(
            httpContext,
            new FakeLease(TimeSpan.FromSeconds(90)),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
        Assert.Equal("90", httpContext.Response.Headers[HeaderNames.RetryAfter].ToString());

        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body);
        var root = document.RootElement;

        Assert.Equal(StatusCodes.Status429TooManyRequests, root.GetProperty("status").GetInt32());
        Assert.Equal("Too many password-reset requests.", root.GetProperty("title").GetString());
        Assert.False(root.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task WriteSubmitRejection_Writes429WithRetryAfterAndProblemDetails()
    {
        var (httpContext, body) = CreateResponseContext();

        await PasswordResetRateLimitPolicy.WriteSubmitRejectionAsync(
            httpContext,
            new FakeLease(TimeSpan.FromSeconds(60)),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
        Assert.Equal("60", httpContext.Response.Headers[HeaderNames.RetryAfter].ToString());

        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body);

        Assert.Equal("Too many password-reset attempts.", document.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public void TheActions_RequireTheirNamedPolicies()
    {
        var request = typeof(AuthController)
            .GetMethod(nameof(AuthController.ForgotPassword))
            ?.GetCustomAttribute<EnableRateLimitingAttribute>();
        var submit = typeof(AuthController)
            .GetMethod(nameof(AuthController.ResetPassword))
            ?.GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.NotNull(request);
        Assert.Equal(PasswordResetRateLimitPolicy.RequestName, request.PolicyName);
        Assert.NotNull(submit);
        Assert.Equal(PasswordResetRateLimitPolicy.SubmitName, submit.PolicyName);
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(-1, 15)]
    [InlineData(1001, 15)]
    [InlineData(5, 0)]
    [InlineData(5, 1441)]
    public void RequestOptions_RejectOutOfRangeValues(int permitLimit, int windowMinutes)
    {
        Assert.False(IsValid(new PasswordResetRequestRateLimitOptions
        {
            PermitLimit = permitLimit,
            WindowMinutes = windowMinutes,
        }));
    }

    [Fact]
    public void Options_DefaultToTheShippedLimits()
    {
        Assert.Equal(3, new PasswordResetRequestRateLimitOptions().PermitLimit);
        Assert.Equal(15, new PasswordResetRequestRateLimitOptions().WindowMinutes);
        Assert.Equal(5, new PasswordResetSubmitRateLimitOptions().PermitLimit);
        Assert.Equal(15, new PasswordResetSubmitRateLimitOptions().WindowMinutes);
    }

    private static bool IsValid(object options)
    {
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        return Validator.TryValidateObject(options, context, results, validateAllProperties: true);
    }

    private static DefaultHttpContext CreateContext(string? email = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(TestIpAddress);

        if (RateLimitEmailKey.Normalize(email) is { } normalized)
        {
            context.Items[RateLimitEmailKey.ItemKey] = normalized;
        }

        return context;
    }

    private static (DefaultHttpContext Context, MemoryStream Body) CreateResponseContext()
    {
        var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        context.Response.Body = body;

        return (context, body);
    }

    private sealed class FakeLease(TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames =>
            retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (metadataName == MetadataName.RetryAfter.Name && retryAfter is { } value)
            {
                metadata = value;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}

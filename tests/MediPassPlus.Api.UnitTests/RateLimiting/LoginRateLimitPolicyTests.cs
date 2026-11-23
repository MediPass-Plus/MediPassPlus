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

public class LoginRateLimitPolicyTests
{
    private const string TestIpAddress = "203.0.113.10";

    [Fact]
    public void CreatePartition_KeysByNormalizedEmailAndClientAddress()
    {
        var context = CreateContext("doctor@example.com");

        var partition = LoginRateLimitPolicy.CreatePartition(context, new LoginRateLimitOptions());

        Assert.Equal($"email:doctor@example.com:ip:{TestIpAddress}", partition.PartitionKey);
    }

    [Fact]
    public void CreatePartition_DoesNotShareAKeyBetweenEmails()
    {
        var first = LoginRateLimitPolicy.CreatePartition(
            CreateContext("first@example.com"),
            new LoginRateLimitOptions());
        var second = LoginRateLimitPolicy.CreatePartition(
            CreateContext("second@example.com"),
            new LoginRateLimitOptions());

        Assert.NotEqual(first.PartitionKey, second.PartitionKey);
    }

    [Fact]
    public void CreatePartition_DoesNotShareAKeyBetweenClientAddresses()
    {
        var first = LoginRateLimitPolicy.CreatePartition(
            CreateContext("patient@example.com", "203.0.113.10"),
            new LoginRateLimitOptions());
        var second = LoginRateLimitPolicy.CreatePartition(
            CreateContext("patient@example.com", "203.0.113.11"),
            new LoginRateLimitOptions());

        Assert.NotEqual(first.PartitionKey, second.PartitionKey);
    }

    [Fact]
    public void CreatePartition_FallsBackToTheClientAddressWhenNoEmailWasCaptured()
    {
        var partition = LoginRateLimitPolicy.CreatePartition(
            CreateContext(email: null),
            new LoginRateLimitOptions());

        Assert.Equal($"ip:{TestIpAddress}", partition.PartitionKey);
    }

    [Theory]
    [InlineData("  Patient@Example.COM  ", "patient@example.com")]
    [InlineData("doctor@example.com", "doctor@example.com")]
    public void NormalizeEmail_TrimsAndLowercases(string value, string expected)
    {
        Assert.Equal(expected, LoginRateLimitPolicy.NormalizeEmail(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeEmail_ReturnsNullForAValueThatCannotKeyABucket(string? value)
    {
        Assert.Null(LoginRateLimitPolicy.NormalizeEmail(value));
    }

    [Fact]
    public void Policy_AllowsThePermitLimitAndRejectsTheNextAttempt()
    {
        var options = new LoginRateLimitOptions { PermitLimit = 2, WindowMinutes = 15 };
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => LoginRateLimitPolicy.CreatePartition(context, options));
        var context = CreateContext("patient@example.com");

        using var first = limiter.AttemptAcquire(context);
        using var second = limiter.AttemptAcquire(context);
        using var third = limiter.AttemptAcquire(context);

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.False(third.IsAcquired);
        Assert.True(third.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void Policy_DoesNotShareAWindowBetweenEmails()
    {
        var options = new LoginRateLimitOptions { PermitLimit = 1, WindowMinutes = 15 };
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => LoginRateLimitPolicy.CreatePartition(context, options));

        using var first = limiter.AttemptAcquire(CreateContext("first@example.com"));
        using var second = limiter.AttemptAcquire(CreateContext("second@example.com"));

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
    }

    [Fact]
    public void LoginAction_RequiresTheNamedPolicy()
    {
        var attribute = typeof(AuthController)
            .GetMethod(nameof(AuthController.Login))
            ?.GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(LoginRateLimitPolicy.Name, attribute.PolicyName);
    }

    [Fact]
    public void Options_DefaultToFiveAttemptsPerFifteenMinutes()
    {
        var options = new LoginRateLimitOptions();

        Assert.Equal(5, options.PermitLimit);
        Assert.Equal(15, options.WindowMinutes);
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(-1, 15)]
    [InlineData(1001, 15)]
    [InlineData(5, 0)]
    [InlineData(5, -1)]
    [InlineData(5, 1441)]
    public void Options_RejectOutOfRangeValues(int permitLimit, int windowMinutes)
    {
        var options = new LoginRateLimitOptions
        {
            PermitLimit = permitLimit,
            WindowMinutes = windowMinutes
        };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(
            options,
            new ValidationContext(options),
            results,
            validateAllProperties: true);

        Assert.False(valid);
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task WriteRejection_Writes429WithRetryAfterAndProblemDetails()
    {
        var (httpContext, body) = CreateResponseContext();

        await LoginRateLimitPolicy.WriteRejectionAsync(
            httpContext,
            new FakeLease(TimeSpan.FromSeconds(90)),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
        Assert.Equal("90", httpContext.Response.Headers[HeaderNames.RetryAfter].ToString());
        Assert.StartsWith("application/problem+json", httpContext.Response.ContentType);

        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body);
        var root = document.RootElement;

        Assert.Equal(StatusCodes.Status429TooManyRequests, root.GetProperty("status").GetInt32());
        Assert.Equal("Too many login attempts.", root.GetProperty("title").GetString());
        Assert.Equal(
            "https://tools.ietf.org/html/rfc6585#section-4",
            root.GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task WriteRejection_WithoutRetryAfterMetadata_StillSetsTheHeader()
    {
        var (httpContext, _) = CreateResponseContext();

        await LoginRateLimitPolicy.WriteRejectionAsync(
            httpContext,
            new FakeLease(retryAfter: null),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
        Assert.Equal("60", httpContext.Response.Headers[HeaderNames.RetryAfter].ToString());
    }

    private static DefaultHttpContext CreateContext(string? email, string ipAddress = TestIpAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ipAddress);

        if (LoginRateLimitPolicy.NormalizeEmail(email) is { } normalized)
        {
            context.Items[LoginRateLimitPolicy.EmailItemKey] = normalized;
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

using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using MediPassPlus.Api.Controllers;
using MediPassPlus.Api.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MediPassPlus.Api.UnitTests.RateLimiting;

public class QrApproveRateLimitPolicyTests
{
    private const string TestIpAddress = "203.0.113.10";

    [Fact]
    public void CreatePartition_KeysAuthenticatedRequestsByPatient()
    {
        var patientId = Guid.NewGuid();

        var partition = QrApproveRateLimitPolicy.CreatePartition(
            CreateContext(patientId),
            new QrApproveRateLimitOptions());

        Assert.Equal($"patient:{patientId}", partition.PartitionKey);
    }

    [Fact]
    public void CreatePartition_DoesNotShareAKeyBetweenPatients()
    {
        var first = QrApproveRateLimitPolicy.CreatePartition(
            CreateContext(Guid.NewGuid()),
            new QrApproveRateLimitOptions());
        var second = QrApproveRateLimitPolicy.CreatePartition(
            CreateContext(Guid.NewGuid()),
            new QrApproveRateLimitOptions());

        Assert.NotEqual(first.PartitionKey, second.PartitionKey);
    }

    [Fact]
    public void CreatePartition_FallsBackToTheRemoteIpForAnonymousCallers()
    {
        var partition = QrApproveRateLimitPolicy.CreatePartition(
            CreateContext(patientId: null),
            new QrApproveRateLimitOptions());

        Assert.Equal($"ip:{TestIpAddress}", partition.PartitionKey);
    }

    [Fact]
    public void Policy_AllowsThePermitLimitAndRejectsTheNextAttempt()
    {
        var options = new QrApproveRateLimitOptions { PermitLimit = 2, WindowMinutes = 15 };
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => QrApproveRateLimitPolicy.CreatePartition(context, options));
        var context = CreateContext(Guid.NewGuid());

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
    public void Policy_DoesNotShareAWindowBetweenPatients()
    {
        var options = new QrApproveRateLimitOptions { PermitLimit = 1, WindowMinutes = 15 };
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => QrApproveRateLimitPolicy.CreatePartition(context, options));

        using var first = limiter.AttemptAcquire(CreateContext(Guid.NewGuid()));
        using var second = limiter.AttemptAcquire(CreateContext(Guid.NewGuid()));

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
    }

    [Fact]
    public void Controller_RequiresTheNamedPolicy()
    {
        var attribute = typeof(AccessRequestsController)
            .GetCustomAttribute<EnableRateLimitingAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(QrApproveRateLimitPolicy.Name, attribute.PolicyName);
    }

    [Fact]
    public void Options_DefaultToFiveAttemptsPerFifteenMinutes()
    {
        var options = new QrApproveRateLimitOptions();

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
        var options = new QrApproveRateLimitOptions
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

        await QrApproveRateLimitPolicy.WriteRejectionAsync(
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
        Assert.Equal("Too many access-request decisions.", root.GetProperty("title").GetString());
        Assert.Equal(
            "https://tools.ietf.org/html/rfc6585#section-4",
            root.GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("patientId", out _));
    }

    [Fact]
    public async Task WriteRejection_WithoutRetryAfterMetadata_StillSetsTheHeader()
    {
        var (httpContext, _) = CreateResponseContext();

        await QrApproveRateLimitPolicy.WriteRejectionAsync(
            httpContext,
            new FakeLease(retryAfter: null),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
        Assert.Equal("60", httpContext.Response.Headers[HeaderNames.RetryAfter].ToString());
    }

    private static DefaultHttpContext CreateContext(Guid? patientId)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(TestIpAddress);

        if (patientId is { } id)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, id.ToString())],
                authenticationType: "Test"));
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

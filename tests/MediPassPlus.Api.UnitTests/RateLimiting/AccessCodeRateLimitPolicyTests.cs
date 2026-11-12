using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using MediPassPlus.Api.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MediPassPlus.Api.UnitTests.RateLimiting;

public class AccessCodeRateLimitPolicyTests
{
    private const string TestIpAddress = "203.0.113.10";

    [Fact]
    public void CreateGenerationPartition_KeysAuthenticatedRequestsByPatient()
    {
        var patientId = Guid.NewGuid();

        var partition = AccessCodeRateLimitPolicy.CreateGenerationPartition(
            CreateContext(patientId, appointmentId: null),
            new CodeGenerationRateLimitOptions());

        Assert.Equal($"patient:{patientId}", partition.PartitionKey);
    }

    [Fact]
    public void CreateGenerationPartition_DoesNotShareAKeyBetweenPatients()
    {
        var first = AccessCodeRateLimitPolicy.CreateGenerationPartition(
            CreateContext(Guid.NewGuid(), appointmentId: null),
            new CodeGenerationRateLimitOptions());
        var second = AccessCodeRateLimitPolicy.CreateGenerationPartition(
            CreateContext(Guid.NewGuid(), appointmentId: null),
            new CodeGenerationRateLimitOptions());

        Assert.NotEqual(first.PartitionKey, second.PartitionKey);
    }

    [Fact]
    public void CreateVerificationPartition_KeysByDoctorAccountAndAppointment()
    {
        var doctorId = Guid.NewGuid();

        var partition = AccessCodeRateLimitPolicy.CreateVerificationPartition(
            CreateContext(doctorId, appointmentId: "42"),
            new CodeVerificationRateLimitOptions());

        Assert.Equal($"doctor:{doctorId}:appointment:42", partition.PartitionKey);
    }

    [Fact]
    public void CreateVerificationPartition_DoesNotShareAKeyBetweenAppointments()
    {
        var doctorId = Guid.NewGuid();

        var first = AccessCodeRateLimitPolicy.CreateVerificationPartition(
            CreateContext(doctorId, appointmentId: "42"),
            new CodeVerificationRateLimitOptions());
        var second = AccessCodeRateLimitPolicy.CreateVerificationPartition(
            CreateContext(doctorId, appointmentId: "43"),
            new CodeVerificationRateLimitOptions());

        Assert.NotEqual(first.PartitionKey, second.PartitionKey);
    }

    [Fact]
    public void CreatePartitions_FallBackToTheRemoteIpForAnonymousCallers()
    {
        var generation = AccessCodeRateLimitPolicy.CreateGenerationPartition(
            CreateContext(callerId: null, appointmentId: null),
            new CodeGenerationRateLimitOptions());
        var verification = AccessCodeRateLimitPolicy.CreateVerificationPartition(
            CreateContext(callerId: null, appointmentId: "42"),
            new CodeVerificationRateLimitOptions());

        Assert.Equal($"ip:{TestIpAddress}", generation.PartitionKey);
        Assert.Equal($"ip:{TestIpAddress}", verification.PartitionKey);
    }

    [Fact]
    public void GenerationPolicy_AllowsThePermitLimitAndRejectsTheNextAttempt()
    {
        var options = new CodeGenerationRateLimitOptions { PermitLimit = 2, WindowMinutes = 15 };
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => AccessCodeRateLimitPolicy.CreateGenerationPartition(context, options));
        var context = CreateContext(Guid.NewGuid(), appointmentId: null);

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
    public void VerificationPolicy_DoesNotShareAWindowBetweenAppointments()
    {
        var options = new CodeVerificationRateLimitOptions { PermitLimit = 1, WindowMinutes = 15 };
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(
            context => AccessCodeRateLimitPolicy.CreateVerificationPartition(context, options));
        var doctorId = Guid.NewGuid();

        using var first = limiter.AttemptAcquire(CreateContext(doctorId, "42"));
        using var second = limiter.AttemptAcquire(CreateContext(doctorId, "43"));
        using var repeated = limiter.AttemptAcquire(CreateContext(doctorId, "42"));

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.False(repeated.IsAcquired);
    }

    [Fact]
    public void Options_DefaultToThreeGenerationsAndFiveVerificationsPerFifteenMinutes()
    {
        var generation = new CodeGenerationRateLimitOptions();
        var verification = new CodeVerificationRateLimitOptions();

        Assert.Equal(3, generation.PermitLimit);
        Assert.Equal(15, generation.WindowMinutes);
        Assert.Equal(5, verification.PermitLimit);
        Assert.Equal(15, verification.WindowMinutes);
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(-1, 15)]
    [InlineData(1001, 15)]
    [InlineData(3, 0)]
    [InlineData(3, -1)]
    [InlineData(3, 1441)]
    public void GenerationOptions_RejectOutOfRangeValues(int permitLimit, int windowMinutes)
    {
        var options = new CodeGenerationRateLimitOptions
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

    [Theory]
    [InlineData(0, 15)]
    [InlineData(-1, 15)]
    [InlineData(1001, 15)]
    [InlineData(5, 0)]
    [InlineData(5, -1)]
    [InlineData(5, 1441)]
    public void VerificationOptions_RejectOutOfRangeValues(int permitLimit, int windowMinutes)
    {
        var options = new CodeVerificationRateLimitOptions
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
    public async Task GenerationRejection_Writes429WithRetryAfterAndProblemDetails()
    {
        var (httpContext, body) = CreateResponseContext();

        await AccessCodeRateLimitPolicy.WriteGenerationRejectionAsync(
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
        Assert.Equal("Too many access-code requests.", root.GetProperty("title").GetString());
        Assert.False(root.TryGetProperty("code", out _));
    }

    [Fact]
    public async Task VerificationRejection_WithoutRetryAfterMetadata_StillSetsTheHeader()
    {
        var (httpContext, _) = CreateResponseContext();

        await AccessCodeRateLimitPolicy.WriteVerificationRejectionAsync(
            httpContext,
            new FakeLease(retryAfter: null),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
        Assert.Equal("60", httpContext.Response.Headers[HeaderNames.RetryAfter].ToString());
    }

    private static DefaultHttpContext CreateContext(Guid? callerId, string? appointmentId)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(TestIpAddress);

        if (appointmentId is not null)
        {
            context.Request.RouteValues["appointmentId"] = appointmentId;
        }

        if (callerId is { } id)
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

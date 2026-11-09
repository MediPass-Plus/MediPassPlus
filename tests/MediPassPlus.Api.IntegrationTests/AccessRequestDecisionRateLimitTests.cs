using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// The per-patient <c>QrApprove</c> fixed window (ADR-0007 decision 2): five decisions per fifteen minutes, a 429
/// with <c>Retry-After</c>, and a separate bucket for every patient.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AccessRequestDecisionRateLimitTests
{
    private const int PermitLimit = 5;
    private const int WindowMinutes = 15;

    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with the shared API factory.</summary>
    /// <param name="factory">The collection fixture that hosts the API and database.</param>
    public AccessRequestDecisionRateLimitTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The sixth decision in the window is rejected before the controller runs.</summary>
    [Fact]
    public async Task SixthDecisionInTheWindow_Returns429WithRetryAfterAndProblemDetails()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(patient);

        for (var attempt = 1; attempt <= PermitLimit; attempt++)
        {
            using var allowed = await PostDecisionAsync(client);
            Assert.Equal(HttpStatusCode.NotFound, allowed.StatusCode);
        }

        using var rejected = await PostDecisionAsync(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        var retryAfter = rejected.Headers.RetryAfter;
        Assert.NotNull(retryAfter);
        Assert.NotNull(retryAfter.Delta);
        Assert.InRange(
            retryAfter.Delta.Value.TotalSeconds,
            1,
            TimeSpan.FromMinutes(WindowMinutes).TotalSeconds);

        var problem = await rejected.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(429, problem.Status);
        Assert.Equal("Too many access-request decisions.", problem.Title);
    }

    /// <summary>Exhausting one patient's window does not affect another patient.</summary>
    [Fact]
    public async Task LimitIsPartitionedPerPatient()
    {
        var limitedPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var limitedClient = _factory.CreateAuthenticatedClient(limitedPatient);

        for (var attempt = 1; attempt <= PermitLimit; attempt++)
        {
            using var allowed = await PostDecisionAsync(limitedClient);
            Assert.Equal(HttpStatusCode.NotFound, allowed.StatusCode);
        }

        using var rejected = await PostDecisionAsync(limitedClient);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var otherClient = _factory.CreateAuthenticatedClient(otherPatient);

        using var otherResponse = await PostDecisionAsync(otherClient);

        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);
        Assert.Null(otherResponse.Headers.RetryAfter);
    }

    private static Task<HttpResponseMessage> PostDecisionAsync(HttpClient client) =>
        client.PostAsync(
            $"/api/access-requests/{Guid.NewGuid()}/reject",
            content: null);
}

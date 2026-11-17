using System.Net;
using System.Text.Json;
using MediPassPlus.Api.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP tests for <c>GET /health</c>: it answers without a token, reports both checks, maps an unhealthy check to
/// <c>503</c>, never leaks failure detail and stays out of the Swagger document (NFR-REL-06, FR-PRV-08, ADR-0013).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class HealthEndpointTests : IClassFixture<MediPassApiFactory>
{
    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with its own API factory and database.</summary>
    /// <param name="factory">The class fixture that hosts this class's API and database.</param>
    public HealthEndpointTests(MediPassApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_WithoutAToken_ReturnsOkWithTheApiAndDatabaseChecks()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty(HealthCheckNames.Api).GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty(HealthCheckNames.Database).GetString());
    }

    [Fact]
    public async Task Get_IsNeverCachedAndCarriesTheCorrelationHeader()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(
            response.Headers.GetValues("X-Correlation-ID").SingleOrDefault()));
    }

    [Fact]
    public async Task Get_WithAnUnhealthyDatabaseCheck_Returns503WithoutTheFailureDetail()
    {
        // The real check against a reachable server is covered by DatabaseHealthCheckTests. Here the check itself is
        // replaced, so the endpoint's 503 contract and the writer's "statuses only" rule are proven deterministically:
        // a bad connection string would also break the role seeding at startup.
        await using var factory = new MediPassApiFactory(
            nameof(HealthEndpointTests),
            "Testing",
            exposeApiReference: false,
            configureTestServices: services =>
                services.Configure<HealthCheckServiceOptions>(options =>
                {
                    options.Registrations.Clear();
                    options.Registrations.Add(new HealthCheckRegistration(
                        HealthCheckNames.Api,
                        new FixedHealthCheck(HealthStatus.Healthy),
                        failureStatus: null,
                        tags: null));
                    options.Registrations.Add(new HealthCheckRegistration(
                        HealthCheckNames.Database,
                        new FixedHealthCheck(HealthStatus.Unhealthy),
                        failureStatus: null,
                        tags: null));
                }));

        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("unreachable", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=", raw, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(raw);
        Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "Unhealthy",
            document.RootElement.GetProperty("checks").GetProperty(HealthCheckNames.Database).GetString());
        Assert.Equal(
            "Healthy",
            document.RootElement.GetProperty("checks").GetProperty(HealthCheckNames.Api).GetString());
    }

    [Fact]
    public async Task Health_IsNotPartOfTheSwaggerDocument()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await ReadJsonAsync(response);
        Assert.False(document.GetProperty("paths").TryGetProperty("/health", out _));
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    private sealed class FixedHealthCheck : IHealthCheck
    {
        private readonly HealthStatus _status;

        public FixedHealthCheck(HealthStatus status) => _status = status;

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_status == HealthStatus.Healthy
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database unreachable."));
    }
}

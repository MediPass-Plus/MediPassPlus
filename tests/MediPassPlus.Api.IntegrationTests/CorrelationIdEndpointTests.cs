using System.Net;
using MediPassPlus.Api.Logging;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// Proves the correlation id crosses the real pipeline: it is echoed on success, on an unknown route, on an
/// unauthorized request and on a <c>500</c> written by the exception handler, and a generated one has the documented
/// shape (NFR-REL-02).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class CorrelationIdEndpointTests : IClassFixture<MediPassApiFactory>
{
    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with its own API factory and database.</summary>
    /// <param name="factory">The class fixture that hosts this class's API and database.</param>
    public CorrelationIdEndpointTests(MediPassApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Request_WithACorrelationHeader_ReturnsTheSameValue()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "pipeline-correlation-1");

        using var response = await client.SendAsync(request);

        Assert.Equal(
            "pipeline-correlation-1",
            response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }

    [Fact]
    public async Task Request_WithoutAHeader_GetsAGeneratedValue()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");

        var id = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.Matches("^[0-9a-f]{32}$", id);
    }

    [Fact]
    public async Task AnUnauthorizedRequest_StillCarriesTheCorrelationHeader()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/me/appointments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.HeaderName));
    }

    [Fact]
    public async Task AnUnknownRoute_StillCarriesTheCorrelationHeader()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/no-such-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.HeaderName));
    }

    [Fact]
    public async Task AThrowingEndpoint_StillCarriesTheCorrelationHeader()
    {
        // The exception handler clears the response before it writes ProblemDetails, so this is the case where an
        // eagerly written header disappears. The id is written from OnStarting instead (NFR-REL-02).
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/test/throw");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "exception-correlation-1");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(
            "exception-correlation-1",
            response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }
}

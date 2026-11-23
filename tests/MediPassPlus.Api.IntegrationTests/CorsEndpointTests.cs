using System.Net;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// The CORS boundary (NFR-SEC-07): only the configured Angular origin is allowed, a preflight from that origin is
/// answered before authentication or rate limiting, and a foreign origin gets no allow-origin header.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class CorsEndpointTests : IClassFixture<MediPassApiFactory>
{
    private const string AngularOrigin = "http://localhost:4200";
    private const string ForeignOrigin = "http://evil.example";

    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with its own API factory and database.</summary>
    /// <param name="factory">The class fixture that hosts this class's API and database.</param>
    public CorsEndpointTests(MediPassApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Preflight_FromTheAngularOrigin_IsAllowed()
    {
        using var client = _factory.CreateClient();
        using var request = CreatePreflightRequest(AngularOrigin);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            AngularOrigin,
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Preflight_FromAForeignOrigin_GetsNoAllowOriginHeader()
    {
        using var client = _factory.CreateClient();
        using var request = CreatePreflightRequest(ForeignOrigin);

        using var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task AnActualRequestFromTheAngularOrigin_CarriesTheAllowOriginHeader()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", AngularOrigin);

        using var response = await client.SendAsync(request);

        Assert.Equal(
            AngularOrigin,
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task AnActualRequestFromAForeignOrigin_IsNotAllowed()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", ForeignOrigin);

        using var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static HttpRequestMessage CreatePreflightRequest(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/Auth/login");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        return request;
    }
}

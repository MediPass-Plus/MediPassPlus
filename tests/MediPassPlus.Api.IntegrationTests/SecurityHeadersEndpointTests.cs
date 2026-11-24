using Microsoft.AspNetCore.Mvc.Testing;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// The response-hardening contract (NFR-SEC-07): every response carries the browser headers, HSTS is advertised on
/// HTTPS outside Development, and an HTTP request carries no HSTS header.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SecurityHeadersEndpointTests : IClassFixture<MediPassApiFactory>
{
    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with its own API factory and database.</summary>
    /// <param name="factory">The class fixture that hosts this class's API and database.</param>
    public SecurityHeadersEndpointTests(MediPassApiFactory factory) => _factory = factory;

    [Fact]
    public async Task EveryResponse_CarriesTheHardeningHeaders()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("camera=(self)", response.Headers.GetValues("Permissions-Policy").Single());
        Assert.Contains(
            "default-src 'self'",
            response.Headers.GetValues("Content-Security-Policy").Single(),
            StringComparison.Ordinal);
        Assert.Contains(
            "frame-ancestors 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Outside Development an HTTPS response advertises HSTS. The host is deliberately not <c>localhost</c>: the
    /// framework skips HSTS for <c>localhost</c>, <c>127.0.0.1</c> and <c>[::1]</c> by default, so that a developer who
    /// serves local HTTPS never pins HSTS in their own browser. That exclusion is the framework's safety default and
    /// the API keeps it, so the assertion uses the kind of host name a deployment answers on.
    /// </summary>
    [Fact]
    public async Task AnHttpsRequest_AdvertisesHstsOutsideDevelopment()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://medipass.example")
        });

        using var response = await client.GetAsync("/health");

        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
        Assert.Contains(
            "max-age=",
            response.Headers.GetValues("Strict-Transport-Security").Single(),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The local-development exclusion is intentional: a localhost response never carries HSTS, so a developer's own
    /// browser is not pinned to HTTPS for every future local project. This is the reason the HTTPS test above uses a
    /// deployment-style host name instead of <c>https://localhost</c>.
    /// </summary>
    [Fact]
    public async Task AnHttpsRequestOnLocalhost_DoesNotCarryHsts()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/health");

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task AnHttpRequest_DoesNotCarryHsts()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }
}

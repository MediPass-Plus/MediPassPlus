using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Infrastructure.Persistence.Seeding;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// Proves the harness itself: the real pipeline answers 401 without a token and 200 for a synthetic user in every
/// application role, so the login helper produces usable tokens for Patient, Doctor, Staff and Admin (NFR-SEC-10,
/// ADR-0006).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SmokeTests
{
    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the smoke tests with the shared API factory.</summary>
    /// <param name="factory">The collection fixture that hosts the API and database.</param>
    public SmokeTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>An anonymous request to an authorized endpoint is refused by the real authentication middleware.</summary>
    [Fact]
    public async Task Probe_WithoutAToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/test/probe");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A synthetic user in each role receives a token that the real bearer validation accepts.</summary>
    /// <param name="role">The application role.</param>
    [Theory]
    [InlineData(DefaultRoles.Patient)]
    [InlineData(DefaultRoles.Doctor)]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task Probe_WithAToken_ReturnsOkForEveryRole(string role)
    {
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await client.GetAsync("/api/test/probe");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var probe = await response.Content.ReadFromJsonAsync<ProbeResponse>();
        Assert.NotNull(probe);
        Assert.Equal(user.Id.ToString(), probe.UserId);
        Assert.Contains(role, probe.Roles);
    }

    private sealed record ProbeResponse(string UserId, string[] Roles);
}

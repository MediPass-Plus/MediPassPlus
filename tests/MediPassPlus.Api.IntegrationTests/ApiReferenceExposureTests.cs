using System.Net;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP smoke tests for the API-reference exposure rule: Development always serves Swagger, every other
/// environment serves it only when <c>OpenApi:ExposeOutsideDevelopment</c> is <c>true</c> (ADR-0012 decision 6).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ApiReferenceExposureTests
{
    /// <summary>Development keeps the local Swagger UI even with the production flag off.</summary>
    [Fact]
    public async Task Swagger_IsServedInDevelopmentWhenTheFlagIsFalse()
    {
        await using var factory = new MediPassApiFactory(null, "Development", exposeApiReference: false);
        using var client = factory.CreateClient();

        await AssertServedAsync(client);
    }

    /// <summary>Production is closed by default: both the document and the UI answer 404.</summary>
    [Fact]
    public async Task Swagger_IsHiddenInProductionWhenTheFlagIsFalse()
    {
        await using var factory = new MediPassApiFactory(null, "Production", exposeApiReference: false);
        using var client = factory.CreateClient();

        await AssertHiddenAsync(client);
    }

    /// <summary>The explicit production opt-in serves the document and the UI.</summary>
    [Fact]
    public async Task Swagger_IsServedInProductionWhenTheFlagIsTrue()
    {
        await using var factory = new MediPassApiFactory(null, "Production", exposeApiReference: true);
        using var client = factory.CreateClient();

        await AssertServedAsync(client);
    }

    private static async Task AssertServedAsync(HttpClient client)
    {
        using var document = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);

        using var ui = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
    }

    private static async Task AssertHiddenAsync(HttpClient client)
    {
        using var document = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.NotFound, document.StatusCode);

        using var ui = await client.GetAsync("/swagger");
        Assert.Equal(HttpStatusCode.NotFound, ui.StatusCode);
    }
}

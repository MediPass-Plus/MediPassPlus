using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// Guards the generated Swagger document against schema-id collisions (ADR-0012). Before the
/// namespace-qualified ids, the document returned 500 because two <c>ClinicDto</c> types shared the
/// bare <c>ClinicDto</c> schema id.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SwaggerDocumentTests : IClassFixture<MediPassApiFactory>
{
    private readonly MediPassApiFactory _factory;

    /// <summary>Creates the test class over its own API host and database.</summary>
    /// <param name="factory">The class fixture that boots the real pipeline.</param>
    public SwaggerDocumentTests(MediPassApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    [Fact]
    public async Task SwaggerDocument_GeneratesAndContainsBothClinicDtoSchemas()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        Assert.True(schemas.TryGetProperty("MediPassPlus_Application_Clinical_ClinicDto", out _));
        Assert.True(schemas.TryGetProperty("MediPassPlus_Application_Dtos_AdminManagement_ClinicDto", out _));
        Assert.True(schemas.TryGetProperty(
            "MediPassPlus_Application_Common_PagedResultOf_MediPassPlus_Application_Clinical_ClinicDto",
            out _));

        var paths = document.RootElement.GetProperty("paths");

        // XML comments must reach the document.
        var clinicsSummary = paths.GetProperty("/api/Clinics").GetProperty("get").GetProperty("summary");
        Assert.False(string.IsNullOrWhiteSpace(clinicsSummary.GetString()));

        // Anonymous operations stay unmarked; [Authorize] operations carry the Bearer requirement.
        var login = paths.GetProperty("/api/Auth/login").GetProperty("post");
        Assert.False(login.TryGetProperty("security", out _));

        var createAdmin = paths.GetProperty("/api/Auth/create-admin").GetProperty("post");
        Assert.True(createAdmin.TryGetProperty("security", out var security));
        Assert.Equal("Bearer", security[0].EnumerateObject().Single().Name);
    }

    /// <summary>The Swagger UI itself loads, not only the JSON document (ADR-0012 decision 2).</summary>
    [Fact]
    public async Task SwaggerUi_LoadsTheDocument()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("swagger-ui", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ADR-0012 keeps exactly one generator: the old built-in OpenAPI route is not mapped, so only the
    /// Swashbuckle document exists.
    /// </summary>
    [Fact]
    public async Task OpenApiV1Route_IsNotMapped()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Every <c>[Authorize]</c> operation carries the Bearer requirement and every anonymous operation stays
    /// unmarked, so the document cannot drift from the endpoint metadata (ADR-0012 decision 4).
    /// </summary>
    [Fact]
    public async Task SecurityRequirement_MatchesTheAuthorizeMetadata()
    {
        using var client = _factory.CreateClient();
        using var document = JsonDocument.Parse(
            await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");

        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint =>
                endpoint.Metadata.GetMetadata<ApiExplorerSettingsAttribute>()?.IgnoreApi != true)
            .ToList();

        Assert.NotEmpty(endpoints);

        foreach (var endpoint in endpoints)
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            if (methods is null || methods.Count == 0 || endpoint.RoutePattern.RawText is null)
            {
                continue;
            }

            var path = NormalizePath(endpoint.RoutePattern.RawText);
            var operation = paths.GetProperty(path);
            var requiresAuthorization = endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null;

            foreach (var method in methods)
            {
                var action = operation.GetProperty(method.ToLowerInvariant());
                var hasSecurity = action.TryGetProperty("security", out var security);

                Assert.True(
                    requiresAuthorization == hasSecurity,
                    $"{method} {path}: expected security={requiresAuthorization}, found {hasSecurity}.");
                if (hasSecurity)
                {
                    Assert.Contains(
                        security.EnumerateArray(),
                        requirement => requirement.TryGetProperty("Bearer", out _));
                }
            }
        }
    }

    private static string NormalizePath(string route)
    {
        var withSlash = route.StartsWith('/') ? route : "/" + route;
        return Regex.Replace(withSlash, @"\{([^}:]+)(?::[^}]*)?\}", "{$1}");
    }
}

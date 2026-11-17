using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// Guards the generated Swagger document: schema-id uniqueness, the UI wiring, the summary and response
/// metadata of every operation, the documented validation failure of login, and the document-wide matching
/// of the Bearer requirement to <c>[Authorize]</c> (ADR-0012).
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

    /// <summary>
    /// The Swagger UI loads and its own configuration points at the document this host serves (ADR-0012
    /// decision 2). Checking only the static HTML shell would not catch a UI wired to a missing URL.
    /// </summary>
    [Fact]
    public async Task SwaggerUi_LoadsTheDocument()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("swagger-ui", html, StringComparison.OrdinalIgnoreCase);

        // index.html boots the UI through index.js, and index.js resolves the relative document URL
        // ("v1/swagger.json") against /swagger/index.html.
        Assert.Contains("index.js", html, StringComparison.OrdinalIgnoreCase);

        using var initializer = await client.GetAsync("/swagger/index.js");
        Assert.Equal(HttpStatusCode.OK, initializer.StatusCode);
        var script = await initializer.Content.ReadAsStringAsync();
        Assert.Contains("v1/swagger.json", script, StringComparison.OrdinalIgnoreCase);

        using var document = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        using var parsed = JsonDocument.Parse(await document.Content.ReadAsStringAsync());
        Assert.True(parsed.RootElement.TryGetProperty("openapi", out _));
    }

    /// <summary>
    /// Every operation documents what it returns: a summary, at least one response, and a description on
    /// each response (ADR-0008 decision 6, kept by ADR-0012). This turns the review checklist into a
    /// regression guard, so a new endpoint cannot ship an undocumented operation.
    /// </summary>
    [Fact]
    public async Task EveryOperationHasASummaryAndDescribedResponses()
    {
        using var client = _factory.CreateClient();
        using var document = JsonDocument.Parse(
            await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");
        var failures = new List<string>();
        var operations = 0;

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.Name.ToLowerInvariant() is not ("get" or "post" or "put"
                    or "delete" or "patch"))
                {
                    continue;
                }

                operations++;

                if (!operation.Value.TryGetProperty("summary", out var summary)
                    || string.IsNullOrWhiteSpace(summary.GetString()))
                {
                    failures.Add($"{operation.Name.ToUpperInvariant()} {path.Name}: missing summary");
                }

                if (!operation.Value.TryGetProperty("responses", out var responses)
                    || responses.ValueKind != JsonValueKind.Object
                    || !responses.EnumerateObject().Any())
                {
                    failures.Add($"{operation.Name.ToUpperInvariant()} {path.Name}: no declared responses");
                    continue;
                }

                foreach (var response in responses.EnumerateObject())
                {
                    if (!response.Value.TryGetProperty("description", out var description)
                        || string.IsNullOrWhiteSpace(description.GetString()))
                    {
                        failures.Add(
                            $"{operation.Name.ToUpperInvariant()} {path.Name} {response.Name}: missing description");
                    }
                }
            }
        }

        Assert.True(operations > 0, "The document contains no operations.");
        Assert.True(
            failures.Count == 0,
            "The document has undocumented operations:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The validation <c>400</c> that <c>LoginDtoValidator</c> makes reachable is both declared in the
    /// document and actually returned for a malformed body (NFR-SEC-06).
    /// </summary>
    [Fact]
    public async Task Login_DocumentsTheValidationFailureItReturns()
    {
        using var client = _factory.CreateClient();
        using var document = JsonDocument.Parse(
            await client.GetStringAsync("/swagger/v1/swagger.json"));

        var responses = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/Auth/login")
            .GetProperty("post")
            .GetProperty("responses");

        Assert.True(
            responses.TryGetProperty("400", out var validation),
            "POST /api/Auth/login can return 400 but the document does not declare it.");
        Assert.False(string.IsNullOrWhiteSpace(validation.GetProperty("description").GetString()));

        using var response = await client.PostAsJsonAsync(
            "/api/Auth/login",
            new { email = "not-an-email", password = string.Empty });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("Email", problem.Errors.Keys);
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

using System.Net;
using System.Text.Json;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// Guards the generated Swagger document against schema-id collisions (ADR-0012). Before the
/// namespace-qualified ids, the document returned 500 because two <c>ClinicDto</c> types shared the
/// bare <c>ClinicDto</c> schema id.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class SwaggerDocumentTests
{
    private readonly MediPassApiFactory _factory;

    /// <summary>Creates the test class over the shared API host.</summary>
    /// <param name="factory">The collection fixture that boots the real pipeline.</param>
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
}

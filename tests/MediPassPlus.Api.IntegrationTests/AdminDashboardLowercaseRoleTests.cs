using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediPassPlus.Infrastructure.Persistence.Seeding;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// Regression for the dashboard's user-count source: self-registration accepts the role in any casing, so the count
/// must come from Identity's canonical role, not from the duplicate <c>ApplicationUser.Role</c> column. This class
/// has its own database fixture, so no canonical patient exists before the registration and the old
/// group-by-<c>Role</c> implementation would report zero (FR-STF-08).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AdminDashboardLowercaseRoleTests : IClassFixture<MediPassApiFactory>
{
    private const string Url = "/api/admin/dashboard";

    private readonly MediPassApiFactory _factory;

    public AdminDashboardLowercaseRoleTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dashboard_CountsAPatientWhoRegisteredWithALowercaseRole()
    {
        var admin = await _factory.CreateUserAsync(DefaultRoles.Admin);
        using var adminClient = _factory.CreateAuthenticatedClient(admin);

        Assert.Equal(0, await ReadPatientsAsync(adminClient));

        using var anonymous = _factory.CreateClient();
        using var register = await anonymous.PostAsJsonAsync(
            "/api/Auth/register",
            new
            {
                fullName = "Lowercase Role Patient",
                email = $"lowercase.{Guid.NewGuid():N}@integration.medipass.test",
                password = "Reg@12345678",
                role = "patient"
            });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        Assert.Equal(1, await ReadPatientsAsync(adminClient));
    }

    private static async Task<int> ReadPatientsAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("users").GetProperty("patients").GetInt32();
    }
}

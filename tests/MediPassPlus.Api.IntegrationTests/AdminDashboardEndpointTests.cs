using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediPassPlus.Application.Dtos.AdminManagement;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP tests for the admin dashboard: Admin-only access, count correctness and a response with counts only
/// (FR-STF-08, FR-STF-09, FR-PRV-06).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AdminDashboardEndpointTests : IClassFixture<MediPassApiFactory>
{
    private const string Url = "/api/admin/dashboard";

    private static readonly string[] ForbiddenWords =
        ["diagnos", "prescription", "report", "medical", "history", "email", "name", "phone", "patientId"];

    private readonly MediPassApiFactory _factory;

    public AdminDashboardEndpointTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    // ---------- Roles ----------

    [Fact]
    public async Task Dashboard_WithoutToken_ReturnsUnauthorizedProblem()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(
            response.Headers.WwwAuthenticate,
            header => header.Scheme == "Bearer");
        await AssertProblemAsync(response, 401);
    }

    [Theory]
    [InlineData(DefaultRoles.Patient)]
    [InlineData(DefaultRoles.Doctor)]
    [InlineData(DefaultRoles.Staff)]
    public async Task Dashboard_WithNonAdminRole_ReturnsForbiddenProblem(string role)
    {
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemAsync(response, 403);
    }

    [Fact]
    public async Task Dashboard_WithAdminRole_ReturnsOk()
    {
        var admin = await _factory.CreateUserAsync(DefaultRoles.Admin);
        using var client = _factory.CreateAuthenticatedClient(admin);

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- Response shape ----------

    [Fact]
    public async Task Dashboard_ReturnsCountsOnly_AndNoMedicalOrPersonalFields()
    {
        await _factory.CreateAppointmentStatusScenarioAsync();
        var admin = await _factory.CreateUserAsync(DefaultRoles.Admin);
        using var client = _factory.CreateAuthenticatedClient(admin);

        using var response = await client.GetAsync(Url);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        AssertProperties(root, "users", "clinics", "appointments");
        AssertProperties(root.GetProperty("users"), "patients", "doctors", "staff", "admins");
        AssertProperties(
            root.GetProperty("appointments"),
            "booked", "checkedIn", "inVisit", "completed", "cancelled");

        AssertAllNumbers(root);

        foreach (var word in ForbiddenWords)
        {
            Assert.DoesNotContain(word, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------- Count correctness ----------

    [Fact]
    public async Task Dashboard_CountsMatchTheDatabase()
    {
        // The factory shares one database per test class, so compare the response with the live counts instead of
        // assuming an empty database. Seed known extra rows first so the numbers are non-trivial.
        await _factory.CreateVisitScenarioAsync(AppointmentStatus.CheckedIn);
        await _factory.CreateVisitScenarioAsync(AppointmentStatus.InVisit);
        await _factory.CreateAppointmentStatusScenarioAsync(AppointmentStatus.Booked);
        await _factory.CreateAppointmentStatusScenarioAsync(AppointmentStatus.Cancelled);

        var admin = await _factory.CreateUserAsync(DefaultRoles.Admin);
        using var client = _factory.CreateAuthenticatedClient(admin);

        using var response = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        await using var db = _factory.CreateDbContext();

        var users = root.GetProperty("users");
        Assert.Equal(await CountActiveInRoleAsync(db, DefaultRoles.Patient), users.GetProperty("patients").GetInt32());
        Assert.Equal(await CountActiveInRoleAsync(db, DefaultRoles.Doctor), users.GetProperty("doctors").GetInt32());
        Assert.Equal(await CountActiveInRoleAsync(db, DefaultRoles.Staff), users.GetProperty("staff").GetInt32());
        Assert.Equal(await CountActiveInRoleAsync(db, DefaultRoles.Admin), users.GetProperty("admins").GetInt32());

        Assert.Equal(await db.Clinics.CountAsync(), root.GetProperty("clinics").GetInt32());

        var appointments = root.GetProperty("appointments");
        Assert.Equal(
            await db.Appointments.CountAsync(a => a.Status == AppointmentStatus.Booked),
            appointments.GetProperty("booked").GetInt32());
        Assert.Equal(
            await db.Appointments.CountAsync(a => a.Status == AppointmentStatus.CheckedIn),
            appointments.GetProperty("checkedIn").GetInt32());
        Assert.Equal(
            await db.Appointments.CountAsync(a => a.Status == AppointmentStatus.InVisit),
            appointments.GetProperty("inVisit").GetInt32());
        Assert.Equal(
            await db.Appointments.CountAsync(a => a.Status == AppointmentStatus.Completed),
            appointments.GetProperty("completed").GetInt32());
        Assert.Equal(
            await db.Appointments.CountAsync(a => a.Status == AppointmentStatus.Cancelled),
            appointments.GetProperty("cancelled").GetInt32());

        // The seeded rows are visible, so the counts are not all zero.
        Assert.True(appointments.GetProperty("checkedIn").GetInt32() >= 1);
        Assert.True(appointments.GetProperty("inVisit").GetInt32() >= 1);
        Assert.True(appointments.GetProperty("booked").GetInt32() >= 1);
        Assert.True(appointments.GetProperty("cancelled").GetInt32() >= 1);
        Assert.True(root.GetProperty("clinics").GetInt32() >= 4);
    }

    [Fact]
    public async Task Dashboard_DoesNotCountADeactivatedAccount()
    {
        var admin = await _factory.CreateUserAsync(DefaultRoles.Admin);
        using var adminClient = _factory.CreateAuthenticatedClient(admin);
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);

        var before = await ReadPatientsAsync(adminClient);

        using var deactivate = await adminClient.PostAsync(
            $"/api/admin/users/{patient.Id}/deactivate",
            content: null);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        var after = await ReadPatientsAsync(adminClient);

        Assert.Equal(before - 1, after);
    }

    // ---------- Guard: a new role or status must be counted ----------

    [Fact]
    public void Dashboard_CoversEveryDefaultRoleAndEveryAppointmentStatus()
    {
        // The DTO exposes one property per role and per status, and the count test maps each one. If a role or a
        // status is added or renamed, this test fails until the dashboard follows.
        var roleProperties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DefaultRoles.Patient] = nameof(UserCountsDto.Patients),
            [DefaultRoles.Doctor] = nameof(UserCountsDto.Doctors),
            [DefaultRoles.Staff] = nameof(UserCountsDto.Staff),
            [DefaultRoles.Admin] = nameof(UserCountsDto.Admins),
        };

        Assert.Equal(
            DefaultRoles.All.OrderBy(role => role).ToArray(),
            roleProperties.Keys.OrderBy(role => role).ToArray());
        Assert.Equal(
            roleProperties.Values.OrderBy(name => name).ToArray(),
            typeof(UserCountsDto).GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name)
                .ToArray());

        Assert.Equal(
            Enum.GetValues<AppointmentStatus>().Select(status => status.ToString()).OrderBy(name => name).ToArray(),
            typeof(AppointmentCountsDto).GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name)
                .ToArray());
    }

    // ---------- Helpers ----------

    private static Task<int> CountActiveInRoleAsync(MediPassDbContext db, string roleName) =>
        (from userRole in db.UserRoles
         join role in db.Roles on userRole.RoleId equals role.Id
         join user in db.Users on userRole.UserId equals user.Id
         where role.Name == roleName && user.IsActive
         select userRole).CountAsync();

    private static async Task<int> ReadPatientsAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("users").GetProperty("patients").GetInt32();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, int status)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(status, doc.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("title").GetString()));
    }

    private static void AssertProperties(JsonElement element, params string[] expected)
    {
        var actual = element.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(expected.OrderBy(n => n).ToArray(), actual);
    }

    private static void AssertAllNumbers(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                AssertAllNumbers(property.Value);
            }
            else
            {
                Assert.Equal(JsonValueKind.Number, property.Value.ValueKind);
            }
        }
    }
}
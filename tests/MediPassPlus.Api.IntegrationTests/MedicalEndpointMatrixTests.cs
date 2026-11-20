using System.Net;
using System.Text;
using System.Text.Json;
using MediPassPlus.Infrastructure.Persistence.Seeding;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// MP-55: the NFR-SEC-10 negative matrix for every medical and visit-access endpoint. Add a new endpoint
/// by adding one row to <see cref="Endpoints"/>.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class MedicalEndpointMatrixTests : IClassFixture<MediPassApiFactory>
{
    private static readonly Guid AnyGuid = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly string[] AllRoles =
        [DefaultRoles.Patient, DefaultRoles.Doctor, DefaultRoles.Staff, DefaultRoles.Admin];

    private readonly MediPassApiFactory _factory;

    public MedicalEndpointMatrixTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    // Method, path, body, allowed roles (comma separated).
    private static readonly (string Method, string Path, string? Body, string Allowed)[] Endpoints =
    [
        ("POST", "/api/appointments/1/diagnoses", """{"description":"x"}""", DefaultRoles.Doctor),
        ("POST", $"/api/diagnoses/{AnyGuid}/prescriptions", """{"medicineName":"x","dosage":"1"}""", DefaultRoles.Doctor),
        ("GET", "/api/appointments/1/history", null, DefaultRoles.Doctor),
        ("GET", "/api/me/history", null, DefaultRoles.Patient),
        ("POST", "/api/appointments/1/start-visit", null, DefaultRoles.Doctor),
        ("POST", "/api/appointments/1/access-code", null, DefaultRoles.Patient),
        ("POST", "/api/appointments/1/verify-code", """{"code":"123456"}""", DefaultRoles.Doctor),
        ("GET", "/api/appointments/1/access-state", null, $"{DefaultRoles.Doctor},{DefaultRoles.Patient}"),
        ("POST", "/api/appointments/1/complete", null, DefaultRoles.Doctor),
        ("POST", "/api/access/scan", """{"token":"x"}""", DefaultRoles.Patient),
        ("POST", "/api/access-requests/scan-image", null, DefaultRoles.Patient),
        ("POST", "/api/access-requests/token/approve", """{"token":"x"}""", DefaultRoles.Patient),
        ("POST", "/api/access-requests/token/reject", """{"token":"x"}""", DefaultRoles.Patient),
        ("POST", $"/api/access-requests/{AnyGuid}/approve", null, DefaultRoles.Patient),
        ("POST", $"/api/access-requests/{AnyGuid}/reject", null, DefaultRoles.Patient),
        ("POST", $"/api/access-grants/{AnyGuid}/end", null, DefaultRoles.Patient),
        ("GET", "/api/doctors/me/schedule?date=2030-01-01", null, DefaultRoles.Doctor)
    ];

    public static IEnumerable<object[]> NoTokenCases() =>
        Endpoints.Select(e => new object[] { e.Method, e.Path, e.Body! });

    public static IEnumerable<object[]> WrongRoleCases()
    {
        foreach (var e in Endpoints)
        {
            var allowed = e.Allowed.Split(',');
            foreach (var role in AllRoles.Where(r => !allowed.Contains(r)))
            {
                yield return [role, e.Method, e.Path, e.Body!];
            }
        }
    }

    [Theory]
    [MemberData(nameof(NoTokenCases))]
    public async Task NoToken_ReturnsUnauthorized(string method, string path, string? body)
    {
        using var client = _factory.CreateClient();

        using var response = await SendAsync(client, method, path, body);

        await AssertProblemBodyAsync(response, HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(WrongRoleCases))]
    public async Task WrongRole_ReturnsForbidden(string role, string method, string path, string? body)
    {
        var token = await _factory.CreateTokenForRoleAsync(role);
        using var client = _factory.CreateAuthenticatedClient(new TestUser(Guid.NewGuid(), role, token));

        using var response = await SendAsync(client, method, path, body);

        await AssertProblemBodyAsync(response, HttpStatusCode.Forbidden);
    }

    private static async Task AssertProblemBodyAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal((int)expected, doc.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("title").GetString()));

        foreach (var marker in new[]
        {
            MediPassApiFactory.HistoryDiagnosisPrefix,
            MediPassApiFactory.HistoryMedicinePrefix,
            MediPassApiFactory.HistoryDosagePrefix,
            MediPassApiFactory.HistoryNotesPrefix
        })
        {
            Assert.DoesNotContain(marker, body, StringComparison.Ordinal);
        }
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path, string? body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return client.SendAsync(request);
    }
}
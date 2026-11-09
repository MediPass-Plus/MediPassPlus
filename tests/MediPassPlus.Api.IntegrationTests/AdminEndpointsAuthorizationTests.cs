using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP authorization matrix for the MP-29 admin endpoints.
///
/// Every admin endpoint must reject Patient, Doctor and Staff with 403 and reject an anonymous
/// caller with 401. The real JWT bearer handler and the real <c>[Authorize(Roles = "Admin")]</c>
/// metadata are exercised; nothing is stubbed.
///
/// MP-29 does not add a medical-record endpoint, so there is no HTTP route that could return
/// medical data to an Admin. <c>MediPassPlus.Api.UnitTests.AdminControllersTests</c> guards the
/// admin route surface against medical/patient route names; the NFR-SEC-10 HTTP matrix will cover
/// medical endpoints when that module lands.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AdminEndpointsAuthorizationTests : IAsyncLifetime
{
    private static readonly Guid TargetUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly (string Method, string Path, string? Body)[] AdminEndpoints =
    [
        ("GET", "/api/admin/clinics", null),
        ("POST", "/api/admin/clinics",
            """{"name":"Integration Clinic","address":"1 Integration Way","phone":"555-0100"}"""),
        ("PUT", "/api/admin/clinics/1",
            """{"name":"Integration Clinic","address":"1 Integration Way","phone":"555-0100"}"""),
        ("POST", "/api/admin/doctors",
            """{"fullName":"Integration Doctor","email":"doctor.integration@medipass.test","password":"Integration1!","specialty":"Cardiology","clinicId":1}"""),
        ("POST", "/api/admin/staff",
            """{"fullName":"Integration Staff","email":"staff.integration@medipass.test","password":"Integration1!","clinicId":1}"""),
        ("POST", $"/api/admin/users/{TargetUserId}/deactivate", null),
        ("POST", $"/api/admin/users/{TargetUserId}/reactivate", null),
        ("POST", "/api/Auth/create-admin",
            """{"fullName":"Integration Admin","email":"admin.integration@medipass.test","password":"Integration1!"}""")
    ];

    private static readonly string[] NonAdminRoles = ["Patient", "Doctor", "Staff"];

    private readonly MediPassApiFactory _factory;
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);
    private HttpClient _client = null!;

    /// <summary>Initializes the test class with the shared API factory.</summary>
    /// <param name="factory">The collection fixture that hosts the API and database.</param>
    public AdminEndpointsAuthorizationTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Builds one anonymous request case for every admin endpoint.</summary>
    /// <returns>The method, path and body of each endpoint.</returns>
    public static IEnumerable<object[]> AnonymousCases()
    {
        foreach (var endpoint in AdminEndpoints)
        {
            yield return new object[] { endpoint.Method, endpoint.Path, endpoint.Body! };
        }
    }

    /// <summary>Builds one non-admin request case for every role and admin endpoint.</summary>
    /// <returns>The role, method, path and body of each case.</returns>
    public static IEnumerable<object[]> NonAdminCases()
    {
        foreach (var role in NonAdminRoles)
        {
            foreach (var endpoint in AdminEndpoints)
            {
                yield return new object[] { role, endpoint.Method, endpoint.Path, endpoint.Body! };
            }
        }
    }

    /// <summary>Mints one token per role and creates the unauthenticated client.</summary>
    /// <returns>A task that completes when the tokens and client are ready.</returns>
    public async Task InitializeAsync()
    {
        foreach (var role in NonAdminRoles.Append("Admin"))
        {
            _tokens[role] = await _factory.CreateTokenForRoleAsync(role);
        }

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    /// <summary>Disposes the test class's HTTP client.</summary>
    /// <returns>A completed task.</returns>
    public Task DisposeAsync()
    {
        _client?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>An anonymous request to any admin endpoint is rejected with 401.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The endpoint path.</param>
    /// <param name="body">The optional JSON body.</param>
    [Theory]
    [MemberData(nameof(AnonymousCases))]
    public async Task AdminEndpoint_WithoutToken_Returns401(string method, string path, string? body)
    {
        var response = await SendAsync(method, path, body, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A Patient, Doctor or Staff token is rejected by every admin endpoint with 403.</summary>
    /// <param name="role">The non-admin role.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The endpoint path.</param>
    /// <param name="body">The optional JSON body.</param>
    [Theory]
    [MemberData(nameof(NonAdminCases))]
    public async Task AdminEndpoint_WithNonAdminRole_Returns403(
        string role, string method, string path, string? body)
    {
        var response = await SendAsync(method, path, body, _tokens[role]);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An Admin token reaches the protected admin surface.</summary>
    [Fact]
    public async Task AdminEndpoint_WithAdminRole_IsAuthorized()
    {
        var response = await SendAsync("GET", "/api/admin/clinics", body: null, _tokens["Admin"]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(
        string method, string path, string? body, string? token)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request);
    }
}

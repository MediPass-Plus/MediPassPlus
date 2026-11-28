using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MediPassPlus.Api.Controllers;
using MediPassPlus.Application.Auth;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// A6: the NFR-SEC-10 authorization matrix for the Accounts surface (FR-ACC-03..06, FR-ACC-09).
///
/// Every endpoint of that surface is anonymous, Admin-only, open to every authenticated role, or Patient-only, and
/// <see cref="Endpoints"/> is the single table that says which. <see cref="EveryAccountsActionHasAMatrixRow"/> reads
/// the real route inventory, so a new Auth, Admin or self-service action fails this class until it gets a row.
///
/// Every case runs the real pipeline: the real JWT bearer handler, the real <c>[Authorize]</c> metadata, the real
/// Identity stores and the real SQL Server database. Nothing is stubbed, and every refusal is checked for the shared
/// problem envelope and for a credential or a stored hash echoed back to the caller (NFR-SEC-06, NFR-PRI-03).
///
/// The rest of the account lifecycle already has HTTP coverage in <c>AuthEndpointTests</c> (password policy,
/// registration roles, lockout) and <c>RefreshTokenEndpointTests</c> (rotation, expiry, session cap, revocation on
/// password reset). This class adds the role-wide matrix, the role-wide lockout, the two session cases the ticket
/// names, and the deactivation window, so the whole matrix is provable from one place.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AccountAuthorizationMatrixTests : IClassFixture<MediPassApiFactory>
{
    private const string Anonymous = "anonymous";
    private const string AllRolesText =
        $"{DefaultRoles.Patient},{DefaultRoles.Doctor},{DefaultRoles.Staff},{DefaultRoles.Admin}";
    private const string WrongPassword = "Wrong0!Pass";
    private const string OtherPassword = "Str0ng!Pass2";
    private const string UnknownRefreshToken = "not-a-token-this-server-ever-issued";
    private const string UnknownEmail = "nobody.authz@integration.medipass.test";
    private const string EchoPassword = "Echo!Passw0rd1";
    private const string EchoToken = "echo-refresh-token-this-server-never-issued";
    private const string NewProfileName = "Authz Matrix";
    private const string ProfilePhone = "5550100";
    private const int TargetClinicId = 1;

    private static readonly Guid TargetUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly string[] AllRoles =
        [DefaultRoles.Patient, DefaultRoles.Doctor, DefaultRoles.Staff, DefaultRoles.Admin];
    private static readonly HttpStatusCode[] AllowedBusinessStatuses =
    [
        HttpStatusCode.OK,
        HttpStatusCode.Created,
        HttpStatusCode.NoContent,
        HttpStatusCode.BadRequest,
        HttpStatusCode.NotFound,
        HttpStatusCode.Conflict,
        HttpStatusCode.TooManyRequests
    ];
    private static readonly string EchoEmail = $"echo.{Guid.NewGuid():N}@integration.medipass.test";

    private static readonly Type[] AccountsControllers =
    [
        typeof(AuthController),
        typeof(AdminClinicsController),
        typeof(AdminAccountsController),
        typeof(AdminUsersController),
        typeof(MeProfileController),
        typeof(MyAuditController)
    ];

    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with its own API factory and database.</summary>
    /// <param name="factory">The class fixture that hosts this class's API and database.</param>
    public AccountAuthorizationMatrixTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    // Method, path, a valid body, and the roles allowed to call it (Anonymous when no token is required).
    private static readonly (string Method, string Path, string? Body, string Allowed)[] Endpoints =
    [
        // The six Auth endpoints that answer without a token: presenting one must never turn them into a challenge.
        ("POST", "/api/Auth/register",
            RegisterBody("authz.matrix.admin@integration.medipass.test", DefaultRoles.Admin), Anonymous),
        ("POST", "/api/Auth/login", LoginBody(UnknownEmail, WrongPassword), Anonymous),
        ("POST", "/api/Auth/refresh", RefreshBody(UnknownRefreshToken), Anonymous),
        ("POST", "/api/Auth/logout", RefreshBody(UnknownRefreshToken), Anonymous),
        ("POST", "/api/Auth/forgot-password", Json(new { email = UnknownEmail }), Anonymous),
        ("POST", "/api/Auth/reset-password", ResetBody(UnknownEmail, EchoToken, OtherPassword), Anonymous),

        // The Admin-only surface: clinic and privileged-account administration plus the second admin bootstrap.
        ("POST", "/api/Auth/create-admin",
            Json(new
            {
                fullName = "Authz Admin",
                email = "authz.create.admin@integration.medipass.test",
                password = MediPassApiFactory.TestPassword
            }),
            DefaultRoles.Admin),
        ("GET", "/api/admin/clinics", null, DefaultRoles.Admin),
        ("POST", "/api/admin/clinics", ClinicBody, DefaultRoles.Admin),
        ("PUT", $"/api/admin/clinics/{TargetClinicId}", ClinicBody, DefaultRoles.Admin),
        ("POST", "/api/admin/doctors", DoctorBody, DefaultRoles.Admin),
        ("POST", "/api/admin/staff", StaffBody, DefaultRoles.Admin),
        ("POST", $"/api/admin/users/{TargetUserId}/deactivate", null, DefaultRoles.Admin),
        ("POST", $"/api/admin/users/{TargetUserId}/reactivate", null, DefaultRoles.Admin),

        // The self-service surface: every authenticated role owns one.
        ("GET", "/api/me/profile", null, AllRolesText),
        ("PUT", "/api/me/profile", ProfileBody, AllRolesText),
        ("POST", "/api/me/change-password", ChangePasswordBody, AllRolesText),

        // The patient-only audit list lives under api/me and is not part of the medical matrix MP-55 added.
        ("GET", "/api/me/audit", null, DefaultRoles.Patient)
    ];

    /// <summary>Every row that needs a token.</summary>
    private static IEnumerable<(string Method, string Path, string? Body, string Allowed)> Protected =>
        Endpoints.Where(endpoint => endpoint.Allowed != Anonymous);

    /// <summary>Builds one anonymous request case for every protected row.</summary>
    /// <returns>The method, path and body of each protected endpoint.</returns>
    public static IEnumerable<object[]> ProtectedWithoutTokenCases() =>
        Protected.Select(endpoint => new object[] { endpoint.Method, endpoint.Path, endpoint.Body! });

    /// <summary>Builds one request case per protected row and role that the row does not allow.</summary>
    /// <returns>The role, method, path and body of each case.</returns>
    public static IEnumerable<object[]> ProtectedWithWrongRoleCases()
    {
        foreach (var endpoint in Protected)
        {
            var allowed = endpoint.Allowed.Split(',');
            foreach (var role in AllRoles.Where(role => !allowed.Contains(role)))
            {
                yield return [role, endpoint.Method, endpoint.Path, endpoint.Body!];
            }
        }
    }

    /// <summary>Builds one request case per protected row and role that the row allows.</summary>
    /// <returns>The role, method, path and body of each case.</returns>
    public static IEnumerable<object[]> ProtectedWithAllowedRoleCases()
    {
        foreach (var endpoint in Protected)
        {
            foreach (var role in endpoint.Allowed.Split(','))
            {
                yield return [role, endpoint.Method, endpoint.Path, endpoint.Body!];
            }
        }
    }

    /// <summary>Builds one request case per anonymous row and role, so no role is ever challenged there.</summary>
    /// <returns>The role, method, path and body of each case.</returns>
    public static IEnumerable<object[]> AnonymousWithEveryRoleCases()
    {
        foreach (var endpoint in Endpoints.Where(endpoint => endpoint.Allowed == Anonymous))
        {
            foreach (var role in AllRoles)
            {
                yield return [role, endpoint.Method, endpoint.Path, endpoint.Body!];
            }
        }
    }

    /// <summary>Builds one refusal case per account endpoint that must not echo what the caller submitted.</summary>
    /// <returns>The method, path, body, expected status and the two markers that must not appear in the body.</returns>
    public static IEnumerable<object[]> CredentialEchoCases()
    {
        yield return
        [
            "POST", "/api/Auth/login", LoginBody(EchoEmail, EchoPassword),
            (int)HttpStatusCode.Unauthorized, EchoPassword, string.Empty
        ];
        yield return
        [
            "POST", "/api/Auth/refresh", RefreshBody(EchoToken),
            (int)HttpStatusCode.Unauthorized, EchoToken, string.Empty
        ];
        yield return
        [
            "POST", "/api/Auth/reset-password", ResetBody(EchoEmail, EchoToken, EchoPassword),
            (int)HttpStatusCode.BadRequest, EchoToken, EchoPassword
        ];
        yield return
        [
            "POST", "/api/Auth/register", RegisterBody(EchoEmail, DefaultRoles.Admin, EchoPassword),
            (int)HttpStatusCode.BadRequest, EchoPassword, string.Empty
        ];
    }

    /// <summary>A protected endpoint refuses an anonymous caller with the shared 401 and a Bearer challenge.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The endpoint path.</param>
    /// <param name="body">The optional JSON body.</param>
    [Theory]
    [MemberData(nameof(ProtectedWithoutTokenCases))]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized(string method, string path, string? body)
    {
        using var client = _factory.CreateClient();

        using var response = await SendAsync(client, method, path, body, token: null);

        await AssertProblemBodyAsync(response, HttpStatusCode.Unauthorized);
        AssertBearerChallenge(response);
    }

    /// <summary>A malformed token is refused exactly like a missing one (NFR-SEC-10).</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The endpoint path.</param>
    /// <param name="body">The optional JSON body.</param>
    [Theory]
    [MemberData(nameof(ProtectedWithoutTokenCases))]
    public async Task ProtectedEndpoint_WithAGarbageToken_ReturnsUnauthorized(string method, string path, string? body)
    {
        using var client = _factory.CreateClient();

        using var response = await SendAsync(client, method, path, body, "not.a.jwt");

        await AssertProblemBodyAsync(response, HttpStatusCode.Unauthorized, "not.a.jwt");
        AssertBearerChallenge(response);
    }

    /// <summary>Every role the endpoint does not allow is refused with 403, never with a redirect or a 404.</summary>
    /// <param name="role">The authenticated role that is not allowed.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The endpoint path.</param>
    /// <param name="body">The optional JSON body.</param>
    [Theory]
    [MemberData(nameof(ProtectedWithWrongRoleCases))]
    public async Task ProtectedEndpoint_WithAWrongRole_ReturnsForbidden(
        string role, string method, string path, string? body)
    {
        var token = await _factory.CreateTokenForRoleAsync(role);
        using var client = _factory.CreateAuthenticatedClient(new TestUser(Guid.NewGuid(), role, token));

        using var response = await SendAsync(client, method, path, body, token: null);

        await AssertProblemBodyAsync(response, HttpStatusCode.Forbidden, token);
    }

    /// <summary>The role the endpoint allows reaches the action: authorization never refuses it.</summary>
    /// <param name="role">The authenticated role that is allowed.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The endpoint path.</param>
    /// <param name="body">The optional JSON body.</param>
    [Theory]
    [MemberData(nameof(ProtectedWithAllowedRoleCases))]
    public async Task ProtectedEndpoint_WithAnAllowedRole_IsNotRefusedByAuthorization(
        string role, string method, string path, string? body)
    {
        var token = await _factory.CreateTokenForRoleAsync(role);
        using var client = _factory.CreateAuthenticatedClient(new TestUser(Guid.NewGuid(), role, token));

        using var response = await SendAsync(client, method, path, body, token: null);

        // The row documents a business answer (200, 201, 204, 400, 404, 409, 429). A 5xx means the action ran and
        // failed, so accepting it would let a broken endpoint pass the matrix (NFR-SEC-10).
        Assert.True(
            AllowedBusinessStatuses.Contains(response.StatusCode),
            $"{method} {path} answered {(int)response.StatusCode}, which is not a documented business status.");
    }

    /// <summary>An anonymous endpoint stays anonymous whatever role is signed in.</summary>
    /// <param name="role">The authenticated role presenting a valid token.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The endpoint path.</param>
    /// <param name="body">The optional JSON body.</param>
    [Theory]
    [MemberData(nameof(AnonymousWithEveryRoleCases))]
    public async Task AnonymousEndpoint_WithAValidToken_IsNotChallenged(
        string role, string method, string path, string? body)
    {
        var token = await _factory.CreateTokenForRoleAsync(role);
        using var client = _factory.CreateAuthenticatedClient(new TestUser(Guid.NewGuid(), role, token));

        using var response = await SendAsync(client, method, path, body, token: null);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // A wrong password is a business refusal; the authorization challenge always carries a Bearer challenge.
            Assert.DoesNotContain(
                response.Headers.WwwAuthenticate,
                header => string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase));
        }

        await AssertNoEchoAsync(response, token);
    }

    /// <summary>
    /// A session opened through the real login endpoint carries the role the endpoints check, for every role: the
    /// token the client actually receives is the one the matrix trusts (FR-ACC-04).
    /// </summary>
    /// <param name="role">The role that signs in.</param>
    [Theory]
    [InlineData(DefaultRoles.Patient)]
    [InlineData(DefaultRoles.Doctor)]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task Login_IssuesATokenTheRoleScopedEndpointsAccept(string role)
    {
        var account = await _factory.CreateUserAsync(role);
        using var anonymous = _factory.CreateClient();

        var session = await LoginAsync(anonymous, account.Email);
        using var authenticated = _factory.CreateAuthenticatedClient(
            new TestUser(account.Id, account.Email, session.AccessToken));

        using var profile = await authenticated.GetAsync("/api/me/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);

        if (role == DefaultRoles.Admin)
        {
            using var clinics = await authenticated.GetAsync("/api/admin/clinics");
            Assert.Equal(HttpStatusCode.OK, clinics.StatusCode);
        }

        if (role == DefaultRoles.Patient)
        {
            using var audit = await authenticated.GetAsync("/api/me/audit");
            Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        }
    }

    /// <summary>
    /// A deactivated account loses login and refresh immediately, while an access token it already holds keeps
    /// working until it expires — the delay ADR-0003 decision 5 accepts because a bearer token is not checked against
    /// the database. Reactivating the account must not revive the session deactivation ended (FR-STF-07).
    /// </summary>
    [Fact]
    public async Task DeactivatedAccount_LosesLoginAndRefreshImmediately_ButKeepsItsAccessTokenUntilItExpires()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var patientClient = _factory.CreateAuthenticatedClient(patient);
        using var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await patientClient.GetAsync("/api/me/profile")).StatusCode);

        var session = await LoginAsync(anonymous, patient.Email);

        var adminToken = await _factory.CreateTokenForRoleAsync(DefaultRoles.Admin);
        using var adminClient = _factory.CreateAuthenticatedClient(
            new TestUser(Guid.NewGuid(), DefaultRoles.Admin, adminToken));

        using var deactivate = await adminClient.PostAsync(
            $"/api/admin/users/{patient.Id}/deactivate", content: null);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        using var refusedLogin = await PostAsync(
            anonymous, "/api/Auth/login", LoginBody(patient.Email, MediPassApiFactory.TestPassword));
        await AssertControllerProblemBodyAsync(refusedLogin, HttpStatusCode.Unauthorized, MediPassApiFactory.TestPassword);

        using var refusedRefresh = await PostAsync(anonymous, "/api/Auth/refresh", RefreshBody(session.RefreshToken));
        await AssertControllerProblemBodyAsync(refusedRefresh, HttpStatusCode.Unauthorized, session.RefreshToken);

        // ADR-0003 decision 5: the flag is not consulted by JWT bearer validation, so the token already issued keeps
        // its 15 minutes. This is the documented window, not a gap this test is meant to close.
        using var stillValid = await patientClient.GetAsync("/api/me/profile");
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);

        using var reactivate = await adminClient.PostAsync(
            $"/api/admin/users/{patient.Id}/reactivate", content: null);
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);

        using var afterReactivation = await PostAsync(
            anonymous, "/api/Auth/refresh", RefreshBody(session.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, afterReactivation.StatusCode);

        using var newLogin = await PostAsync(
            anonymous, "/api/Auth/login", LoginBody(patient.Email, MediPassApiFactory.TestPassword));
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    /// <summary>
    /// Five wrong passwords lock the account of every role, and the correct password stays refused while the lockout
    /// lasts (FR-ACC-05). Identity owns the counter; the test reads it back from the store.
    /// </summary>
    /// <param name="role">The role whose account is locked out.</param>
    [Theory]
    [InlineData(DefaultRoles.Patient)]
    [InlineData(DefaultRoles.Doctor)]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task Login_FiveWrongPasswords_LockTheAccountOfEveryRole(string role)
    {
        var account = await _factory.CreateUserAsync(role);
        await EnableLockoutAsync(account.Id);

        using var client = _factory.CreateClient();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var failed = await PostAsync(client, "/api/Auth/login", LoginBody(account.Email, WrongPassword));
            await AssertControllerProblemBodyAsync(failed, HttpStatusCode.Unauthorized, WrongPassword);
        }

        using var correctPassword = await PostAsync(
            client, "/api/Auth/login", LoginBody(account.Email, MediPassApiFactory.TestPassword));
        await AssertControllerProblemBodyAsync(correctPassword, HttpStatusCode.Unauthorized);

        Assert.True(await IsLockedOutAsync(account.Id));
    }

    /// <summary>
    /// Presenting a token that was already rotated is the signature of a leaked copy, so every live session of the
    /// account ends with the refusal (FR-ACC-06).
    /// </summary>
    [Fact]
    public async Task Refresh_WithAnAlreadyRotatedToken_EndsEveryLiveSession()
    {
        var account = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateClient();

        var first = await LoginAsync(client, account.Email);
        var second = await LoginAsync(client, account.Email);

        using var rotated = await PostAsync(client, "/api/Auth/refresh", RefreshBody(first.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        using var replay = await PostAsync(client, "/api/Auth/refresh", RefreshBody(first.RefreshToken));
        await AssertControllerProblemBodyAsync(replay, HttpStatusCode.Unauthorized, first.RefreshToken);

        // Containment: the independent session died with the replay, so a thief gains nothing from it.
        using var otherSession = await PostAsync(client, "/api/Auth/refresh", RefreshBody(second.RefreshToken));
        await AssertControllerProblemBodyAsync(otherSession, HttpStatusCode.Unauthorized, second.RefreshToken);
    }

    /// <summary>
    /// A logout ends the session that presented the token and nothing else, and the endpoint stays anonymous and
    /// idempotent (FR-ACC-06, ADR-0003 decision 4).
    /// </summary>
    [Fact]
    public async Task Logout_EndsOnlyThePresentedSession_AndIsIdempotent()
    {
        var account = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateClient();

        var first = await LoginAsync(client, account.Email);
        var second = await LoginAsync(client, account.Email);

        using var logout = await PostAsync(client, "/api/Auth/logout", RefreshBody(first.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var ended = await PostAsync(client, "/api/Auth/refresh", RefreshBody(first.RefreshToken));
        await AssertControllerProblemBodyAsync(ended, HttpStatusCode.Unauthorized, first.RefreshToken);

        // A logout-revoked row has no successor, so it is not treated as a leaked copy: the other session survives.
        using var survivor = await PostAsync(client, "/api/Auth/refresh", RefreshBody(second.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, survivor.StatusCode);

        using var again = await PostAsync(client, "/api/Auth/logout", RefreshBody(first.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    /// <summary>
    /// A role supplied in the registration body cannot buy a privileged session: <c>Admin</c>, <c>Doctor</c>
    /// and <c>Staff</c> are refused before any row is written, because only patients register themselves
    /// (FR-ACC-03, FR-ACC-09).
    /// </summary>
    /// <param name="role">The privileged role requested in the body.</param>
    [Theory]
    [InlineData(DefaultRoles.Admin)]
    [InlineData(DefaultRoles.Doctor)]
    [InlineData(DefaultRoles.Staff)]
    public async Task Register_WithAPrivilegedRoleInTheBody_NeverYieldsAPrivilegedSession(string role)
    {
        var email = NewEmail($"register.{role}");
        using var client = _factory.CreateClient();

        using var registered = await PostAsync(
            client, "/api/Auth/register", RegisterBody(email, role));
        Assert.Equal(HttpStatusCode.BadRequest, registered.StatusCode);
        await AssertNoEchoAsync(registered, MediPassApiFactory.TestPassword);

        // The refusal happens before the user row, so there is no account to sign in with and no role to carry.
        Assert.Null(await FindUserAsync(email));

        using var login = await PostAsync(client, "/api/Auth/login", LoginBody(email, MediPassApiFactory.TestPassword));
        await AssertControllerProblemBodyAsync(login, HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// No refusal repeats the submitted credential back to the caller: the password of a login, a refresh or a reset
    /// token, or the password of a refused registration (NFR-SEC-06, NFR-PRI-03).
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The endpoint path.</param>
    /// <param name="body">The refused body.</param>
    /// <param name="expectedStatus">The status the endpoint answers with.</param>
    /// <param name="markerA">A submitted value that must not come back.</param>
    /// <param name="markerB">A second submitted value that must not come back, or the empty string.</param>
    [Theory]
    [MemberData(nameof(CredentialEchoCases))]
    public async Task Refusal_NeverEchoesTheSubmittedCredential(
        string method, string path, string body, int expectedStatus, string markerA, string markerB)
    {
        using var client = _factory.CreateClient();

        using var response = await SendAsync(client, method, path, body, token: null);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        await AssertNoEchoAsync(response, markerA, markerB);
    }

    /// <summary>
    /// The server keeps only the SHA-256 hash of a refresh token and never returns it: the login response carries the
    /// opaque token, the stored row carries the hash, and a later refusal repeats neither (NFR-SEC-06, NFR-PRI-03).
    /// </summary>
    [Fact]
    public async Task StoredTokenHash_IsNeverReturnedOrEchoed()
    {
        var account = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateClient();

        using var login = await PostAsync(
            client, "/api/Auth/login", LoginBody(account.Email, MediPassApiFactory.TestPassword));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var loginBody = await login.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(loginBody);
        var plaintext = document.RootElement.GetProperty("refreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(plaintext));

        await using var db = _factory.CreateDbContext();
        var stored = await db.RefreshTokens.SingleAsync(row => row.UserId == account.Id);

        Assert.Equal(RefreshTokenHasher.Hash(plaintext!), stored.TokenHash);
        Assert.NotEqual(plaintext, stored.TokenHash);
        Assert.DoesNotContain(stored.TokenHash, loginBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TokenHash", loginBody, StringComparison.OrdinalIgnoreCase);

        using var refused = await PostAsync(client, "/api/Auth/refresh", RefreshBody(UnknownRefreshToken));
        await AssertControllerProblemBodyAsync(refused, HttpStatusCode.Unauthorized, stored.TokenHash);
    }

    /// <summary>
    /// A wrong current password is a failure of the request body, not of the session: the answer is the shared 400 and
    /// neither password is echoed, so a stolen access token cannot be used to confirm the current password
    /// (FR-ACC-08, MP-32).
    /// </summary>
    [Fact]
    public async Task ChangePasswordRefusal_DoesNotEchoEitherPassword_AndLeavesTheSessionAlive()
    {
        var account = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(account);

        using var response = await PostAsync(
            client,
            "/api/me/change-password",
            Json(new { currentPassword = "Curr3nt!Marker", newPassword = "N3w!Marker4" }));

        await AssertControllerProblemBodyAsync(
            response,
            HttpStatusCode.BadRequest,
            MediPassApiFactory.TestPassword,
            "Curr3nt!Marker",
            "N3w!Marker4");

        using var profile = await client.GetAsync("/api/me/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
    }

    /// <summary>
    /// The matrix table above is complete: every action of the Accounts controllers has a row, so a new Auth, Admin
    /// or self-service endpoint cannot ship without its 401/403/positive cases (NFR-SEC-10).
    /// </summary>
    [Fact]
    public void EveryAccountsActionHasAMatrixRow()
    {
        var provider = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>();

        var rows = Endpoints
            .Select(endpoint => $"{endpoint.Method.ToUpperInvariant()} {NormalizePath(endpoint.Path)}")
            .ToHashSet(StringComparer.Ordinal);

        var actions = provider.ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(action => AccountsControllers.Contains(action.ControllerTypeInfo.AsType()))
            .ToList();

        Assert.Equal(
            AccountsControllers.Length,
            actions.Select(action => action.ControllerTypeInfo.AsType()).Distinct().Count());

        foreach (var action in actions)
        {
            var template = action.AttributeRouteInfo?.Template;
            Assert.False(string.IsNullOrWhiteSpace(template));

            var methods = action.ActionConstraints?
                .OfType<HttpMethodActionConstraint>()
                .SelectMany(constraint => constraint.HttpMethods)
                .ToList() ?? [];

            Assert.NotEmpty(methods);

            foreach (var method in methods)
            {
                Assert.True(
                    rows.Contains($"{method} {NormalizePath(template!)}"),
                    $"The accounts matrix has no row for {method} {template}.");
            }
        }
    }

    /// <summary>
    /// Collapses concrete identifiers and route parameters to one placeholder, so a template such as
    /// <c>api/admin/clinics/{id:int}</c> matches the <c>/api/admin/clinics/1</c> row that exercises it.
    /// </summary>
    /// <param name="path">A route template or a request path.</param>
    /// <returns>The comparable, lower-case shape of the path.</returns>
    private static string NormalizePath(string path) =>
        string.Join(
            '/',
            path.Trim('/').Split('/').Select(segment =>
                segment.StartsWith('{') || Guid.TryParse(segment, out _) || int.TryParse(segment, out _)
                    ? "{}"
                    : segment.ToLowerInvariant()));

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static string RegisterBody(string email, string role, string password = MediPassApiFactory.TestPassword) =>
        Json(new { fullName = NewProfileName, email, password, role });

    private static string LoginBody(string email, string password) => Json(new { email, password });

    private static string RefreshBody(string refreshToken) => Json(new { refreshToken });

    private static string ResetBody(string email, string token, string newPassword) =>
        Json(new { email, token, newPassword });

    private static string ClinicBody =>
        Json(new { name = "Authz Clinic", address = "1 Authz Way", phone = ProfilePhone });

    private static string DoctorBody =>
        Json(new
        {
            fullName = "Authz Doctor",
            email = "authz.doctor.admin@integration.medipass.test",
            password = MediPassApiFactory.TestPassword,
            specialty = "Cardiology",
            clinicId = TargetClinicId
        });

    private static string StaffBody =>
        Json(new
        {
            fullName = "Authz Staff",
            email = "authz.staff.admin@integration.medipass.test",
            password = MediPassApiFactory.TestPassword,
            clinicId = TargetClinicId
        });

    private static string ProfileBody => Json(new { fullName = NewProfileName, phoneNumber = ProfilePhone });

    private static string ChangePasswordBody =>
        Json(new { currentPassword = WrongPassword, newPassword = OtherPassword });

    private static string NewEmail(string prefix) =>
        $"{prefix}.{Guid.NewGuid():N}@integration.medipass.test";

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string body) =>
        client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));

    private async Task<TokenPair> LoginAsync(HttpClient client, string email)
    {
        using var response = await PostAsync(
            client, "/api/Auth/login", LoginBody(email, MediPassApiFactory.TestPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tokens = await response.Content.ReadFromJsonAsync<TokenPair>();
        Assert.NotNull(tokens);
        Assert.False(string.IsNullOrWhiteSpace(tokens!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));

        return tokens;
    }

    private async Task EnableLockoutAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId.ToString());

        Assert.NotNull(user);

        var result = await users.SetLockoutEnabledAsync(user!, true);
        Assert.True(result.Succeeded);
    }

    private async Task<bool> IsLockedOutAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId.ToString());

        Assert.NotNull(user);
        return await users.IsLockedOutAsync(user!);
    }

    private async Task<ApplicationUser?> FindUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        return await users.FindByEmailAsync(email);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, string method, string path, string? body, string? token)
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

        return await client.SendAsync(request);
    }

    private static void AssertBearerChallenge(HttpResponseMessage response) =>
        Assert.Contains(
            response.Headers.WwwAuthenticate,
            header => string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase));

    private static Task AssertProblemBodyAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        params string[] forbiddenMarkers) =>
        AssertProblemBodyAsync(response, expected, requireTraceId: false, forbiddenMarkers);

    private static Task AssertControllerProblemBodyAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        params string[] forbiddenMarkers) =>
        AssertProblemBodyAsync(response, expected, requireTraceId: true, forbiddenMarkers);

    private static async Task AssertProblemBodyAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        bool requireTraceId,
        string[] forbiddenMarkers)
    {
        Assert.Equal(expected, response.StatusCode);

        // RFC 7807 has one media type. Accepting application/json would hide a refusal that bypassed the shared
        // ProblemDetails envelope (ADR-0010).
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal((int)expected, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));

        if (requireTraceId)
        {
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        }

        AssertNoEcho(body, forbiddenMarkers);
    }

    private static async Task AssertNoEchoAsync(HttpResponseMessage response, params string[] markers)
    {
        AssertNoEcho(await response.Content.ReadAsStringAsync(), markers);
    }

    private static void AssertNoEcho(string body, string[] markers)
    {
        // No credential and no stored hash is ever repeated to the caller (NFR-SEC-06, NFR-PRI-03).
        Assert.DoesNotContain(MediPassApiFactory.TestPassword, body, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TokenHash", body, StringComparison.OrdinalIgnoreCase);

        foreach (var marker in markers.Where(marker => !string.IsNullOrEmpty(marker)))
        {
            Assert.DoesNotContain(marker, body, StringComparison.Ordinal);
        }
    }

    /// <summary>The pair a successful login hands back, read by the tests that need a real session.</summary>
    private sealed record TokenPair(string AccessToken, string RefreshToken);
}

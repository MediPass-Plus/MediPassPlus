using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP contract of the Accounts login, registration and password-reset endpoints (FR-ACC-02,
/// FR-ACC-05, FR-ACC-09).
///
/// Every case runs against the real pipeline: the real Identity stores, the real
/// <c>CheckPasswordSignInAsync</c> lockout counter and the real ProblemDetails envelope. Each test
/// registers its own account so a lockout never leaks into another test.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AuthEndpointTests : IAsyncLifetime, IClassFixture<MediPassApiFactory>
{
    private const string CompliantPassword = "Str0ng!Pass1";
    private const string NewCompliantPassword = "Str0ng!Pass2";
    private const string WrongPassword = "Wrong0!Pass";

    private readonly MediPassApiFactory _factory;
    private HttpClient _client = null!;

    /// <summary>Initializes the test class with its own API factory and database.</summary>
    /// <param name="factory">The class fixture that hosts this class's API and database.</param>
    public AuthEndpointTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Creates the HTTP client used by every case.</summary>
    /// <returns>A completed task.</returns>
    public Task InitializeAsync()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        return Task.CompletedTask;
    }

    /// <summary>Disposes the test class's HTTP client.</summary>
    /// <returns>A completed task.</returns>
    public Task DisposeAsync()
    {
        _client?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>A valid password returns a token (regression guard for the login rewrite).</summary>
    [Fact]
    public async Task Login_WithCorrectPassword_ReturnsAccessToken()
    {
        var email = NewEmail("login.ok");
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(email)).StatusCode);

        var response = await LoginAsync(email, CompliantPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        Assert.Contains("Patient", body.Roles!, StringComparer.Ordinal);
    }

    /// <summary>Four wrong passwords do not lock the account, and the correct one still works.</summary>
    [Fact]
    public async Task Login_FourWrongPasswordsThenTheCorrectOne_Succeeds()
    {
        var email = NewEmail("login.four");
        await RegisterAsync(email);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var failed = await LoginAsync(email, WrongPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var succeeded = await LoginAsync(email, CompliantPassword);

        Assert.Equal(HttpStatusCode.OK, succeeded.StatusCode);
    }

    /// <summary>The fifth wrong password locks the account, so the correct one is refused.</summary>
    [Fact]
    public async Task Login_FifthWrongPassword_LocksTheAccountAndRefusesTheCorrectPassword()
    {
        var email = NewEmail("login.lock");
        await RegisterAsync(email);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var failed = await LoginAsync(email, WrongPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var refused = await LoginAsync(email, CompliantPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        // The lockout is persisted on the account, not simulated by the endpoint.
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);

        Assert.NotNull(user);
        Assert.True(await userManager.IsLockedOutAsync(user!));
    }

    /// <summary>A lockout and a wrong password answer with the same body, so neither leaks state.</summary>
    [Fact]
    public async Task Login_LockedAccount_ReturnsTheSameBodyAsAWrongPassword()
    {
        var email = NewEmail("login.locked.body");
        await RegisterAsync(email);

        var wrongPassword = await ReadProblemAsync(await LoginAsync(email, WrongPassword));

        for (var attempt = 2; attempt <= 5; attempt++)
        {
            await LoginAsync(email, WrongPassword);
        }

        var locked = await ReadProblemAsync(await LoginAsync(email, CompliantPassword));

        Assert.Equal(wrongPassword, locked);
    }

    /// <summary>An unknown address answers exactly like a wrong password, so it is not an enumeration oracle.</summary>
    [Fact]
    public async Task Login_UnknownEmail_ReturnsTheSameBodyAsAWrongPassword()
    {
        var email = NewEmail("login.known");
        await RegisterAsync(email);

        var wrongPassword = await ReadProblemAsync(await LoginAsync(email, WrongPassword));
        var unknownEmail = await ReadProblemAsync(await LoginAsync(NewEmail("login.unknown"), WrongPassword));

        Assert.Equal(wrongPassword, unknownEmail);
    }

    /// <summary>A deactivated account cannot log in and does not reveal that it exists.</summary>
    [Fact]
    public async Task Login_InactiveAccount_ReturnsTheSameBodyAsAWrongPassword()
    {
        // A deactivated account must answer exactly like a wrong password, so the response cannot confirm
        // that the address exists. Registration no longer creates Doctor accounts, so seed the inactive user.
        var inactive = await _factory.CreateUserAsync(DefaultRoles.Doctor, isActive: false);

        var wrongPassword = await ReadProblemAsync(await LoginAsync(NewEmail("login.reference"), WrongPassword));
        var response = await LoginAsync(inactive.Email, MediPassApiFactory.TestPassword);
        var inactiveProblem = await ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(wrongPassword, inactiveProblem);
    }

    /// <summary>A password without a lowercase letter is rejected up front, not by Identity later.</summary>
    [Fact]
    public async Task Register_WithPasswordMissingALowercaseLetter_Returns400ValidationProblem()
    {
        var email = NewEmail("register.lowercase");

        var response = await RegisterAsync(email, "AHMED1234!");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.True(problem!.Errors.ContainsKey("Password"));
        Assert.Contains(
            problem.Errors["Password"],
            message => message.Contains("lowercase", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A compliant password still registers, so the stricter rules are not over-tightened.</summary>
    [Fact]
    public async Task Register_WithCompliantPassword_Succeeds()
    {
        var response = await RegisterAsync(NewEmail("register.ok"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The Admin branch cannot be bypassed by casing. Identity resolves a role name case-insensitively,
    /// so <c>admin</c> and <c>ADMIN</c> are the same role and must be refused exactly like <c>Admin</c>,
    /// and a refused registration creates no account (FR-ACC-09).
    /// </summary>
    /// <param name="role">The requested role, in a casing that must be resolved to the Admin role.</param>
    [Theory]
    [InlineData("Admin")]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    public async Task Register_WithAdminRoleInAnyCasing_RefusesSelfAssignment(string role)
    {
        var email = NewEmail("register.admin");

        var response = await RegisterAsync(email, role: role);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadProblemAsync(response);
        Assert.Equal((int)HttpStatusCode.BadRequest, problem.Status);
        Assert.Equal("This role cannot be self-assigned.", problem.Title);
        Assert.Equal(
            "The Admin role is granted by an existing administrator, never by public registration.",
            problem.Detail);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await userManager.FindByEmailAsync(email));
    }

    /// <summary>
    /// A Doctor or Staff account cannot self-register in any casing: those accounts are created by an
    /// administrator (FR-ACC-03), and the refusal writes no user row.
    /// </summary>
    /// <param name="role">The requested role, in a casing that must be resolved to Doctor or Staff.</param>
    [Theory]
    [InlineData("Doctor")]
    [InlineData("doctor")]
    [InlineData("DOCTOR")]
    [InlineData("Staff")]
    [InlineData("staff")]
    [InlineData("STAFF")]
    public async Task Register_WithADoctorOrStaffRole_RefusesSelfRegistration(string role)
    {
        var email = NewEmail("register.privileged");

        var response = await RegisterAsync(email, role: role);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadProblemAsync(response);
        Assert.Equal((int)HttpStatusCode.BadRequest, problem.Status);
        Assert.Equal("This role cannot be self-assigned.", problem.Title);
        Assert.Equal(
            "Only patients can register themselves. Doctor and Staff accounts are created by an administrator.",
            problem.Detail);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await userManager.FindByEmailAsync(email));
    }

    /// <summary>
    /// The removed development registration route is gone: the commented-out endpoint was deleted, so the old path
    /// answers <c>404</c> instead of creating an account outside the patient-only contract.
    /// </summary>
    [Fact]
    public async Task RegisterDevelopmentRoute_IsGone()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/Auth/register-Development%20Version",
            new
            {
                fullName = "Removed Development Endpoint",
                email = NewEmail("register.development"),
                password = CompliantPassword,
                role = "Patient"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// A patient is the only role that can self-register, in every casing, and the account is active and
    /// owns the Patient role. The duplicate <c>ApplicationUser.Role</c> column is no longer written from
    /// the request body (FR-ACC-03, FR-ACC-05).
    /// </summary>
    /// <param name="role">The requested role, in a casing that must be resolved to the Patient role.</param>
    [Theory]
    [InlineData("Patient")]
    [InlineData("patient")]
    [InlineData("PATIENT")]
    public async Task Register_WithPatientRoleInAnyCasing_CreatesAnActiveAccount(string role)
    {
        var email = NewEmail("register.patient");

        var response = await RegisterAsync(email, role: role);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);

        Assert.NotNull(user);
        Assert.True(user!.IsActive);
        Assert.Equal(string.Empty, user.Role);
        Assert.True(await userManager.IsInRoleAsync(user, DefaultRoles.Patient));
    }

    /// <summary>An unknown role is still refused before any account is created.</summary>
    [Fact]
    public async Task Register_WithAnUnknownRole_Returns400()
    {
        var email = NewEmail("register.unknown");

        var response = await RegisterAsync(email, role: "Wizard");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadProblemAsync(response);
        Assert.Equal((int)HttpStatusCode.BadRequest, problem.Status);
        Assert.Equal("The requested role does not exist.", problem.Title);
        Assert.Equal("Role 'Wizard' does not exist.", problem.Detail);
    }

    /// <summary>
    /// Proving control of the mailbox clears the lockout: a reset on a locked account lets the owner
    /// sign in immediately with the new password (FR-ACC-05).
    /// </summary>
    [Fact]
    public async Task ResetPassword_OnALockedAccount_ClearsTheLockout()
    {
        var email = NewEmail("reset.locked");
        await RegisterAsync(email);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await LoginAsync(email, WrongPassword);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, CompliantPassword)).StatusCode);

        string resetToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            resetToken = await userManager.GeneratePasswordResetTokenAsync(user!);
        }

        var reset = await _client.PostAsJsonAsync(
            "/api/Auth/reset-password",
            new { email, token = resetToken, newPassword = NewCompliantPassword });

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, NewCompliantPassword)).StatusCode);
    }

    /// <summary>
    /// The configuration the application actually runs with matches the accounts policy. This is the
    /// guard that keeps the Identity rules and the DTO validators from drifting apart again.
    /// </summary>
    [Fact]
    public void IdentityOptions_EnforceTheAccountsPolicy()
    {
        var options = _factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.Equal(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), options.Lockout.DefaultLockoutTimeSpan);
        Assert.True(options.Lockout.AllowedForNewUsers);

        Assert.Equal(8, options.Password.RequiredLength);
        Assert.True(options.Password.RequireDigit);
        Assert.True(options.Password.RequireLowercase);
        Assert.True(options.Password.RequireUppercase);
        Assert.True(options.Password.RequireNonAlphanumeric);
    }

    private static string NewEmail(string prefix) =>
        $"{prefix}.{Guid.NewGuid():N}@integration.medipass.test";

    private async Task<HttpResponseMessage> RegisterAsync(
        string email, string password = CompliantPassword, string role = "Patient") =>
        await _client.PostAsJsonAsync(
            "/api/Auth/register",
            new { fullName = "Auth Policy Test", email, password, role });

    private async Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        await _client.PostAsJsonAsync("/api/Auth/login", new { email, password });

    private static async Task<AuthProblem> ReadProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);

        return new AuthProblem(problem!.Status, problem.Title, problem.Detail);
    }

    /// <summary>The fields every refusal must share. <c>traceId</c> is per request and is not compared.</summary>
    private readonly record struct AuthProblem(int? Status, string? Title, string? Detail);

    private sealed record LoginResponse(string AccessToken, string[]? Roles);
}

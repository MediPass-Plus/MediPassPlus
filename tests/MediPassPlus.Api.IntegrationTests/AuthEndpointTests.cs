using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Domain.Identity;
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
public sealed class AuthEndpointTests : IAsyncLifetime
{
    private const string CompliantPassword = "Str0ng!Pass1";
    private const string NewCompliantPassword = "Str0ng!Pass2";
    private const string WrongPassword = "Wrong0!Pass";

    private readonly MediPassApiFactory _factory;
    private HttpClient _client = null!;

    /// <summary>Initializes the test class with the shared API factory.</summary>
    /// <param name="factory">The collection fixture that hosts the API and database.</param>
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
        var email = NewEmail("login.inactive");

        // Registering as a Doctor leaves IsActive = false until an admin activates the account.
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(email, role: "Doctor")).StatusCode);

        var wrongPassword = await ReadProblemAsync(await LoginAsync(NewEmail("login.reference"), WrongPassword));
        var response = await LoginAsync(email, CompliantPassword);
        var inactive = await ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(wrongPassword, inactive);
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

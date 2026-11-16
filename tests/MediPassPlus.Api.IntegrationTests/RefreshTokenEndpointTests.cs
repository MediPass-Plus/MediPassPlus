using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Application.AdminManagement;
using MediPassPlus.Application.Auth;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP contract of the refresh-token lifecycle (FR-ACC-04, FR-ACC-06, NFR-SEC-10): the login hands out a
/// single-use refresh token, a refresh rotates it, a replayed token ends every live session of the account, and a
/// logout answers <c>204</c> whether the token existed or not.
///
/// Every case registers its own account, so the tokens of one case can never be reached by another.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class RefreshTokenEndpointTests : IAsyncLifetime, IClassFixture<MediPassApiFactory>
{
    private const string Password = "Str0ng!Pass1";
    private const string NewPassword = "N3w!Passw0rd2";
    private const string GenericRefreshTitle = "Invalid or expired refresh token.";

    private readonly MediPassApiFactory _factory;
    private HttpClient _client = null!;

    /// <summary>Initializes the test class with the shared API factory.</summary>
    /// <param name="factory">The collection fixture that hosts the API and database.</param>
    public RefreshTokenEndpointTests(MediPassApiFactory factory)
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

    /// <summary>The login response carries the refresh token and the moment it stops being accepted.</summary>
    [Fact]
    public async Task Login_ReturnsTheRefreshTokenAndItsExpiry()
    {
        var tokens = await RegisterAndLoginAsync("refresh.login");

        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.True(tokens.RefreshTokenExpiresAtUtc > DateTime.UtcNow);
        Assert.Equal(15 * 60, tokens.AccessTokenExpiresIn);
        Assert.Contains("Patient", tokens.Roles, StringComparer.Ordinal);
    }

    /// <summary>A valid token is exchanged exactly once and the replacement is a different value.</summary>
    [Fact]
    public async Task Refresh_WithAValidToken_RotatesItAndReturnsANewPair()
    {
        var login = await RegisterAndLoginAsync("refresh.rotate");

        var response = await RefreshAsync(login.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rotated = await response.Content.ReadFromJsonAsync<TokenPair>();
        Assert.NotNull(rotated);
        Assert.NotEqual(login.RefreshToken, rotated!.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(rotated.AccessToken));

        // The replacement is usable and rotates again, so every exchange changes the value.
        var third = await RotateAsync(rotated.RefreshToken);
        Assert.NotEqual(rotated.RefreshToken, third);

        // The presented value is retired: it can never be exchanged a second time.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(login.RefreshToken)).StatusCode);
    }

    /// <summary>
    /// Presenting a token that was already rotated is the signature of a stolen copy, so every live token of the
    /// account is revoked before the refusal (FR-ACC-06).
    /// </summary>
    [Fact]
    public async Task Refresh_WithARotatedToken_RevokesEveryLiveTokenOfTheAccount()
    {
        var email = NewEmail("refresh.replay");
        var login = await LoginAsync(email, afterRegister: true);

        var second = await RotateAsync(login.RefreshToken);
        var third = await RotateAsync(second);

        // The retired login token is presented again: the server treats it as a leaked copy.
        var replay = await RefreshAsync(login.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // Containment: the newest legitimate token died with the replay, so a thief holding it gains nothing.
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(third)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(second)).StatusCode);

        // A new login is the only way back in.
        Assert.False(string.IsNullOrWhiteSpace((await LoginAsync(email)).RefreshToken));
    }

    /// <summary>An unknown token is refused with the single generic body the client is allowed to see.</summary>
    [Fact]
    public async Task Refresh_WithAnUnknownToken_ReturnsTheGeneric401()
    {
        var response = await RefreshAsync("not-a-token-this-server-ever-issued");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await ReadProblemAsync(response);
        Assert.Equal(GenericRefreshTitle, problem.Title);
    }

    /// <summary>A token whose lifetime elapsed is refused, even though its row still exists.</summary>
    [Fact]
    public async Task Refresh_WithAnExpiredToken_Returns401()
    {
        var login = await RegisterAndLoginAsync("refresh.expired");

        Assert.Equal(
            1,
            await UpdateTokenAsync(login.RefreshToken, expiresAtUtc: DateTime.UtcNow.AddMinutes(-1)));

        var response = await RefreshAsync(login.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(GenericRefreshTitle, (await ReadProblemAsync(response)).Title);
    }

    /// <summary>
    /// The thirty-day session cap is checked on every refresh: a token that outlives the whole session is refused
    /// even while its own expiry is still in the future.
    /// </summary>
    [Fact]
    public async Task Refresh_PastTheSessionCap_Returns401()
    {
        var login = await RegisterAndLoginAsync("refresh.sessioncap");

        Assert.Equal(
            1,
            await UpdateTokenAsync(
                login.RefreshToken,
                sessionStartedAtUtc: DateTime.UtcNow.AddDays(-31)));

        var response = await RefreshAsync(login.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A deactivated account cannot extend its session with a token it already holds.</summary>
    [Fact]
    public async Task Refresh_ForADeactivatedAccount_Returns401()
    {
        var email = NewEmail("refresh.inactive");
        var login = await LoginAsync(email, afterRegister: true);

        await DeactivateAsync(email);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(login.RefreshToken)).StatusCode);
    }

    /// <summary>
    /// Deactivation ends the session for good: reactivating the account must not bring the old token back to life
    /// (FR-STF-07, FR-ACC-06). This goes through the same service the admin endpoint calls.
    /// </summary>
    [Fact]
    public async Task Deactivation_ThenReactivation_LeavesTheOldTokenRefused()
    {
        var email = NewEmail("refresh.reactivate");
        var login = await LoginAsync(email, afterRegister: true);

        await SetActiveThroughTheAdminServiceAsync(email, isActive: false);
        await SetActiveThroughTheAdminServiceAsync(email, isActive: true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(login.RefreshToken)).StatusCode);

        // The account itself still works: only the session it held was ended.
        Assert.False(string.IsNullOrWhiteSpace((await LoginAsync(email)).RefreshToken));
    }

    /// <summary>
    /// A password reset ends every live session, so a token stolen before the reset cannot outlive it (FR-ACC-06).
    /// </summary>
    [Fact]
    public async Task ResetPassword_RevokesTheOldRefreshToken()
    {
        var email = NewEmail("refresh.reset");
        var login = await LoginAsync(email, afterRegister: true);
        var resetToken = await GeneratePasswordResetTokenAsync(email);

        var reset = await _client.PostAsJsonAsync(
            "/api/Auth/reset-password",
            new { email, token = resetToken, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(login.RefreshToken)).StatusCode);

        // The new password opens a new session.
        var newLogin = await _client.PostAsJsonAsync(
            "/api/Auth/login",
            new { email, password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    /// <summary>Token responses are never cacheable (RFC 6749 section 5.1).</summary>
    [Fact]
    public async Task LoginAndRefresh_DoNotAllowTheResponseToBeCached()
    {
        var email = NewEmail("refresh.cache");
        var registration = await _client.PostAsJsonAsync(
            "/api/Auth/register",
            new { fullName = "Refresh Token Test", email, password = Password, role = "Patient" });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        var login = await _client.PostAsJsonAsync("/api/Auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal("no-store", SingleHeader(login, "Cache-Control"));

        var tokens = await login.Content.ReadFromJsonAsync<TokenPair>();
        Assert.NotNull(tokens);

        var refresh = await RefreshAsync(tokens!.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal("no-store", SingleHeader(refresh, "Cache-Control"));
    }

    /// <summary>After a logout the same token is refused, and the answer is still the generic one.</summary>
    [Fact]
    public async Task Logout_ThenRefreshWithTheSameToken_Returns401()
    {
        var login = await RegisterAndLoginAsync("refresh.logout");

        Assert.Equal(HttpStatusCode.NoContent, (await LogoutAsync(login.RefreshToken)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(login.RefreshToken)).StatusCode);
    }

    /// <summary>Logging out one device leaves the account's other sessions untouched.</summary>
    [Fact]
    public async Task Logout_OnOneSession_LeavesTheOtherSessionsAlive()
    {
        var email = NewEmail("refresh.multi");

        var first = (await LoginAsync(email, afterRegister: true)).RefreshToken;
        var second = (await LoginAsync(email)).RefreshToken;

        Assert.Equal(HttpStatusCode.NoContent, (await LogoutAsync(first)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(second)).StatusCode);
    }

    /// <summary>A logout is idempotent: the second call answers <c>204</c> like the first.</summary>
    [Fact]
    public async Task Logout_Twice_Returns204BothTimes()
    {
        var login = await RegisterAndLoginAsync("refresh.logout.twice");

        Assert.Equal(HttpStatusCode.NoContent, (await LogoutAsync(login.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LogoutAsync(login.RefreshToken)).StatusCode);
    }

    /// <summary>An unknown token answers <c>204</c>, so a logout cannot be used to discover which tokens exist.</summary>
    [Fact]
    public async Task Logout_WithAnUnknownToken_Returns204()
    {
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await LogoutAsync("not-a-token-this-server-ever-issued")).StatusCode);
    }

    /// <summary>An empty body is a validation failure, not a credential check.</summary>
    [Fact]
    public async Task Refresh_WithoutAToken_Returns400ValidationProblem()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/Auth/refresh",
            new { refreshToken = string.Empty });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.True(problem!.Errors.ContainsKey("RefreshToken"));
    }

    private static string NewEmail(string prefix) =>
        $"{prefix}.{Guid.NewGuid():N}@integration.medipass.test";

    /// <summary>Registers a patient and returns the pair its first login produced.</summary>
    private async Task<TokenPair> RegisterAndLoginAsync(string prefix)
    {
        var email = NewEmail(prefix);

        var registration = await _client.PostAsJsonAsync(
            "/api/Auth/register",
            new { fullName = "Refresh Token Test", email, password = Password, role = "Patient" });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        return await LoginAsync(email);
    }

    private async Task<TokenPair> LoginAsync(string email, bool afterRegister = false)
    {
        if (afterRegister)
        {
            var registration = await _client.PostAsJsonAsync(
                "/api/Auth/register",
                new { fullName = "Refresh Token Test", email, password = Password, role = "Patient" });
            Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        }

        var response = await _client.PostAsJsonAsync(
            "/api/Auth/login",
            new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tokens = await response.Content.ReadFromJsonAsync<TokenPair>();
        Assert.NotNull(tokens);

        return tokens!;
    }

    private async Task<string> RotateAsync(string refreshToken)
    {
        var response = await RefreshAsync(refreshToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rotated = await response.Content.ReadFromJsonAsync<TokenPair>();
        Assert.NotNull(rotated);

        return rotated!.RefreshToken;
    }

    private Task<HttpResponseMessage> RefreshAsync(string? refreshToken) =>
        _client.PostAsJsonAsync("/api/Auth/refresh", new { refreshToken });

    private Task<HttpResponseMessage> LogoutAsync(string? refreshToken) =>
        _client.PostAsJsonAsync("/api/Auth/logout", new { refreshToken });

    /// <summary>
    /// Moves a stored token outside its lifetime or outside the session cap, the way time passing would. Going
    /// through the table keeps the HTTP case independent of the clock the API runs on.
    /// </summary>
    private async Task<int> UpdateTokenAsync(
        string refreshToken,
        DateTime? expiresAtUtc = null,
        DateTime? sessionStartedAtUtc = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediPassDbContext>();
        var hash = RefreshTokenHasher.Hash(refreshToken);
        var token = db.RefreshTokens.Where(row => row.TokenHash == hash);

        if (expiresAtUtc.HasValue)
        {
            var expiry = expiresAtUtc.Value;

            return await token.ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.ExpiresAtUtc, expiry));
        }

        if (sessionStartedAtUtc.HasValue)
        {
            var sessionStart = sessionStartedAtUtc.Value;

            return await token.ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.SessionStartedAtUtc, sessionStart));
        }

        throw new InvalidOperationException("The test asked for no change to the stored token.");
    }

    private async Task DeactivateAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);

        Assert.NotNull(user);
        user!.IsActive = false;

        var result = await userManager.UpdateAsync(user);
        Assert.True(result.Succeeded);
    }

    private async Task SetActiveThroughTheAdminServiceAsync(string email, bool isActive)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var status = scope.ServiceProvider.GetRequiredService<IAccountStatusService>();

        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);

        var failure = await status.SetActiveAsync(
            user!.Id,
            isActive,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(ChangeStatusFailure.None, failure);
    }

    private async Task<string> GeneratePasswordResetTokenAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);

        var token = await userManager.GeneratePasswordResetTokenAsync(user!);
        Assert.False(string.IsNullOrWhiteSpace(token));

        return token;
    }

    private static string? SingleHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? string.Join(", ", values)
            : null;

    private static async Task<AuthProblem> ReadProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);

        return new AuthProblem(problem!.Status, problem.Title, problem.Detail);
    }

    /// <summary>The pair the login and refresh endpoints answer with.</summary>
    private sealed record TokenPair(
        string AccessToken,
        string RefreshToken,
        DateTime RefreshTokenExpiresAtUtc,
        int AccessTokenExpiresIn,
        string[] Roles);

    /// <summary>The fields every refusal must share. <c>traceId</c> is per request and is not compared.</summary>
    private readonly record struct AuthProblem(int? Status, string? Title, string? Detail);
}

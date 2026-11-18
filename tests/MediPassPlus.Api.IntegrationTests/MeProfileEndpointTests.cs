using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediPassPlus.Application.Dtos.Auth;
using MediPassPlus.Application.Dtos.Profile;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP contract of the self-service profile endpoints (FR-ACC-07, FR-ACC-08, US-03, US-04, UC-03).
///
/// Every case runs against the real pipeline and the real Identity store, so the tests prove what the ticket
/// promises: the account is always the token's account, a body cannot choose another account, a password
/// change needs the current password plus the password policy, and a successful change ends every live session.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class MeProfileEndpointTests : IClassFixture<MediPassApiFactory>
{
    // The password MediPassApiFactory gives every account it creates.
    private const string CurrentPassword = "Integration1!";
    private const string NewPassword = "Str0ng!Pass2";

    private const string ProfileRoute = "api/me/profile";
    private const string ChangePasswordRoute = "api/me/change-password";

    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class over its own API host and database.</summary>
    /// <param name="factory">The class fixture that boots the real pipeline.</param>
    public MeProfileEndpointTests(MediPassApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _factory = factory;
    }

    [Theory]
    [InlineData(DefaultRoles.Patient)]
    [InlineData(DefaultRoles.Doctor)]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task GetProfile_AnswersEveryRole_WithItsOwnProfile(string role)
    {
        var user = await _factory.CreateUserAsync(role, $"{role} Profile Owner");
        using var client = _factory.CreateAuthenticatedClient(user);

        var response = await client.GetAsync(ProfileRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await response.Content.ReadFromJsonAsync<ProfileResponse>();
        Assert.NotNull(profile);
        Assert.Equal($"{role} Profile Owner", profile.FullName);
        Assert.Equal(user.Email, profile.Email);
        Assert.Equal(role, profile.Role);
        Assert.Null(profile.PhoneNumber);
    }

    [Fact]
    public async Task GetProfile_WithoutAToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(ProfileRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_WithAnInvalidToken_Returns401()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");

        var response = await client.GetAsync(ProfileRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProfile_SavesTheTrimmedNameAndPhoneNumber()
    {
        var user = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(user);

        var response = await client.PutAsJsonAsync(
            ProfileRoute,
            new UpdateProfileRequest("  Ahmed Updated  ", " +201012345678 "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await LoadUserAsync(user.Id);
        Assert.Equal("Ahmed Updated", stored.FullName);
        Assert.Equal("+201012345678", stored.PhoneNumber);
        Assert.False(stored.PhoneNumberConfirmed);
    }

    [Fact]
    public async Task UpdateProfile_ReturnsTheStoredProfile()
    {
        var user = await _factory.CreateUserAsync(DefaultRoles.Doctor);
        using var client = _factory.CreateAuthenticatedClient(user);

        var response = await client.PutAsJsonAsync(
            ProfileRoute,
            new UpdateProfileRequest("Doctor Updated", "01012345678"));

        var profile = await response.Content.ReadFromJsonAsync<ProfileResponse>();

        Assert.NotNull(profile);
        Assert.Equal("Doctor Updated", profile.FullName);
        Assert.Equal("01012345678", profile.PhoneNumber);
        Assert.Equal(user.Email, profile.Email);
        Assert.Equal(DefaultRoles.Doctor, profile.Role);
    }

    [Fact]
    public async Task UpdateProfile_ClearsThePhoneNumber_WhenTheBodySendsAnEmptyOne()
    {
        var user = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(user);

        var first = await client.PutAsJsonAsync(ProfileRoute, new UpdateProfileRequest("Patient", "01012345678"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PutAsJsonAsync(ProfileRoute, new UpdateProfileRequest("Patient", "  "));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var stored = await LoadUserAsync(user.Id);
        Assert.Null(stored.PhoneNumber);
    }

    [Fact]
    public async Task UpdateProfile_IgnoresIdEmailRoleAndStatusInTheBody()
    {
        var victim = await _factory.CreateUserAsync(DefaultRoles.Patient, "Victim Patient");
        var attacker = await _factory.CreateUserAsync(DefaultRoles.Patient, "Attacker Patient");
        using var client = _factory.CreateAuthenticatedClient(attacker);

        // The extra members are not part of the request contract; they must not move the write onto another
        // account or escalate the caller (FR-ACC-07, NFR-SEC-04).
        var body = new
        {
            id = victim.Id,
            email = "attacker.owns@integration.medipass.test",
            role = DefaultRoles.Admin,
            isActive = false,
            fullName = "Attacker Patient",
            phoneNumber = "01012345678"
        };

        var response = await client.PutAsJsonAsync(ProfileRoute, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var victimAfter = await LoadUserAsync(victim.Id);
        Assert.Equal("Victim Patient", victimAfter.FullName);
        Assert.Null(victimAfter.PhoneNumber);
        Assert.Equal(victim.Email, victimAfter.Email);
        Assert.True(victimAfter.IsActive);

        var attackerAfter = await LoadUserAsync(attacker.Id);
        Assert.Equal(attacker.Email, attackerAfter.Email);
        Assert.True(attackerAfter.IsActive);
        Assert.Equal(DefaultRoles.Patient, await SingleRoleOfAsync(attackerAfter.Id));
    }

    [Fact]
    public async Task UpdateProfile_OfOneUser_DoesNotTouchAnother()
    {
        var first = await _factory.CreateUserAsync(DefaultRoles.Patient, "First Patient");
        var second = await _factory.CreateUserAsync(DefaultRoles.Patient, "Second Patient");

        using var firstClient = _factory.CreateAuthenticatedClient(first);
        var response = await firstClient.PutAsJsonAsync(
            ProfileRoute,
            new UpdateProfileRequest("First Patient Renamed", "01011111111"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var secondClient = _factory.CreateAuthenticatedClient(second);
        var secondProfile = await secondClient.GetFromJsonAsync<ProfileResponse>(ProfileRoute);

        Assert.NotNull(secondProfile);
        Assert.Equal("Second Patient", secondProfile.FullName);
        Assert.Null(secondProfile.PhoneNumber);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateProfile_WithAnEmptyName_Returns400(string fullName)
    {
        var user = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(user);

        var response = await client.PutAsJsonAsync(ProfileRoute, new UpdateProfileRequest(fullName, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey(nameof(UpdateProfileRequest.FullName)));

        var stored = await LoadUserAsync(user.Id);
        Assert.NotEqual(fullName, stored.FullName);
        Assert.Equal($"{DefaultRoles.Patient} Integration Test", stored.FullName);
    }

    [Theory]
    [InlineData("phone")]
    [InlineData("010123")]
    [InlineData("010-123-4567")]
    public async Task UpdateProfile_WithAnInvalidPhone_Returns400(string phoneNumber)
    {
        var user = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(user);

        var response = await client.PutAsJsonAsync(ProfileRoute, new UpdateProfileRequest("Patient", phoneNumber));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey(nameof(UpdateProfileRequest.PhoneNumber)));

        var stored = await LoadUserAsync(user.Id);
        Assert.Null(stored.PhoneNumber);
    }

    [Fact]
    public async Task UpdateProfile_WithoutAToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(ProfileRoute, new UpdateProfileRequest("Nobody", null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithTheCurrentPassword_ReplacesIt()
    {
        var user = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(user);

        var response = await client.PostAsJsonAsync(
            ChangePasswordRoute,
            new ChangePasswordRequest(CurrentPassword, NewPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await LoadUserAsync(user.Id);
        Assert.True(await CheckPasswordAsync(stored.Id, NewPassword));
        Assert.False(await CheckPasswordAsync(stored.Id, CurrentPassword));
    }

    [Fact]
    public async Task ChangePassword_WithAWrongCurrentPassword_Returns400()
    {
        var user = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(user);

        var response = await client.PostAsJsonAsync(
            ChangePasswordRoute,
            new ChangePasswordRequest("Wrong0!Pass", NewPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("current password", problem.Detail, StringComparison.OrdinalIgnoreCase);

        var stored = await LoadUserAsync(user.Id);
        Assert.True(await CheckPasswordAsync(stored.Id, CurrentPassword));
        Assert.False(await CheckPasswordAsync(stored.Id, NewPassword));
    }

    [Theory]
    [InlineData("weak")]
    [InlineData("Sh0rt!")]
    [InlineData("Str0ngPass1")]
    public async Task ChangePassword_WithANewPasswordThatBreaksThePolicy_Returns400(string newPassword)
    {
        var user = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(user);

        var response = await client.PostAsJsonAsync(
            ChangePasswordRoute,
            new ChangePasswordRequest(CurrentPassword, newPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey(nameof(ChangePasswordRequest.NewPassword)));

        Assert.True(await CheckPasswordAsync(user.Id, CurrentPassword));
    }

    [Fact]
    public async Task ChangePassword_ToTheSamePassword_Returns400()
    {
        var user = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(user);

        var response = await client.PostAsJsonAsync(
            ChangePasswordRoute,
            new ChangePasswordRequest(CurrentPassword, CurrentPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey(nameof(ChangePasswordRequest.NewPassword)));
    }

    [Fact]
    public async Task ChangePassword_WithoutAToken_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            ChangePasswordRoute,
            new ChangePasswordRequest(CurrentPassword, NewPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_BeyondTheConfiguredLimit_Returns429()
    {
        var user = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(user);

        // Every attempt spends the account's budget, successful or not: the limit exists to stop a stolen access
        // token from guessing the current password (NFR-SEC-06).
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var refused = await client.PostAsJsonAsync(
                ChangePasswordRoute,
                new ChangePasswordRequest("Wrong0!Pass", NewPassword));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }

        var rejected = await client.PostAsJsonAsync(
            ChangePasswordRoute,
            new ChangePasswordRequest(CurrentPassword, NewPassword));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);

        var problem = await rejected.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(429, problem.Status);

        // The request never reached the controller, so the password is untouched.
        Assert.True(await CheckPasswordAsync(user.Id, CurrentPassword));
    }

    [Fact]
    public async Task ChangePassword_RevokesTheOldRefreshToken()
    {
        var email = $"profile.revoke.{Guid.NewGuid():N}@integration.medipass.test";
        using var client = _factory.CreateClient();

        var registration = await client.PostAsJsonAsync(
            "api/Auth/register",
            new
            {
                fullName = "Profile Revoke",
                email,
                password = CurrentPassword,
                role = DefaultRoles.Patient
            });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        // Two devices sign in, so the change has to end both sessions, not only the one that changed the password.
        var firstSession = await LoginAsync(client, email, CurrentPassword);
        var secondSession = await LoginAsync(client, email, CurrentPassword);

        using var authenticated = _factory.CreateClient();
        authenticated.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", firstSession.AccessToken);

        var change = await authenticated.PostAsJsonAsync(
            ChangePasswordRoute,
            new ChangePasswordRequest(CurrentPassword, NewPassword));
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        // A token that leaked before the change must not outlive it (FR-ACC-06, FR-ACC-08).
        foreach (var refreshToken in new[] { firstSession.RefreshToken, secondSession.RefreshToken })
        {
            var refresh = await client.PostAsJsonAsync("api/Auth/refresh", new { refreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        }

        // The account still works: the new password opens a new session.
        var newSession = await LoginAsync(client, email, NewPassword);
        Assert.False(string.IsNullOrWhiteSpace(newSession.RefreshToken));
    }

    private static async Task<AuthTokensResponse> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("api/Auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tokens = await response.Content.ReadFromJsonAsync<AuthTokensResponse>();
        Assert.NotNull(tokens);

        return tokens!;
    }

    private async Task<ApplicationUser> LoadUserAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await users.FindByIdAsync(userId.ToString());

        Assert.NotNull(user);
        return user;
    }

    private async Task<string> SingleRoleOfAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await users.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);

        return Assert.Single(await users.GetRolesAsync(user));
    }

    private async Task<bool> CheckPasswordAsync(Guid userId, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await users.FindByIdAsync(userId.ToString());
        Assert.NotNull(user);

        return await users.CheckPasswordAsync(user, password);
    }
}

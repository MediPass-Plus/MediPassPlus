using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Api.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// The per-email-and-client <c>Login</c> fixed window (ADR-0007 decision 2, FR-PRV-07): a request over the configured
/// budget gets a <c>429</c> with <c>Retry-After</c> and the shared ProblemDetails envelope, the budget is shared by
/// every spelling of one address, and another address from the same client is unaffected. The per-account lockout is
/// still the authoritative rule and is proved by
/// <see cref="AuthEndpointTests.Login_FifthWrongPassword_LocksTheAccountAndRefusesTheCorrectPassword"/>, which runs
/// against the harness's higher budget so the lockout — not this limiter — answers.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class LoginRateLimitTests : IAsyncLifetime
{
    private const int PermitLimit = 3;

    private readonly MediPassApiFactory _factory = new(
        nameof(LoginRateLimitTests),
        "Testing",
        exposeApiReference: true,
        configureTestServices: services => services.Configure<LoginRateLimitOptions>(
            options =>
            {
                options.PermitLimit = PermitLimit;
                options.WindowMinutes = 15;
            }));

    /// <summary>Nothing to prepare before the factory's host is used.</summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Drops this class's database and restores the environment variables the factory changed.</summary>
    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task TheAttemptAfterTheBudget_Returns429WithRetryAfterAndNoEmailInTheBody()
    {
        using var client = _factory.CreateClient();
        var email = NewEmail();

        for (var attempt = 1; attempt <= PermitLimit; attempt++)
        {
            using var allowed = await LoginAsync(client, email);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var rejected = await LoginAsync(client, email);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.True(int.Parse(retryAfter!.Single(), CultureInfo.InvariantCulture) > 0);

        var body = await rejected.Content.ReadAsStringAsync();
        Assert.DoesNotContain(email, body, StringComparison.OrdinalIgnoreCase);

        var problem = await rejected.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status429TooManyRequests, problem.Status);
        Assert.Equal("Too many login attempts.", problem.Title);
    }

    [Fact]
    public async Task AnotherEmailFromTheSameClient_IsNotThrottled()
    {
        using var client = _factory.CreateClient();
        var throttled = NewEmail();
        var unaffected = NewEmail();

        for (var attempt = 1; attempt <= PermitLimit; attempt++)
        {
            using var allowed = await LoginAsync(client, throttled);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var rejected = await LoginAsync(client, throttled);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        using var other = await LoginAsync(client, unaffected);
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
    }

    [Fact]
    public async Task TheSameAddressInAnotherCase_SharesOneBudget()
    {
        using var client = _factory.CreateClient();
        var email = NewEmail();

        for (var attempt = 1; attempt <= PermitLimit; attempt++)
        {
            using var allowed = await LoginAsync(client, email);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var rejected = await LoginAsync(client, email.ToUpperInvariant());
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    private static string NewEmail() => $"login.limit.{Guid.NewGuid():N}@example.com";

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("api/Auth/login", new { email, password = "Wrong0!Pass" });
}

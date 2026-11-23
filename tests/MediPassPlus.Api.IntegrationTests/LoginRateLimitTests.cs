using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
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
        var (throttled, unaffected) = NewEmailPair();

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

    /// <summary>
    /// A body that repeats <c>email</c> spends the budget of the last value, which is the one MVC binds into the login
    /// action. Reading the first value instead let a caller keep the account constant and rotate a decoy to get a fresh
    /// bucket per request (FR-PRV-07). The lockout still caps password checks, but the limiter itself was defeated.
    /// </summary>
    [Fact]
    public async Task DuplicateEmailProperties_SpendTheBudgetOfTheLastValue()
    {
        using var client = _factory.CreateClient();
        var victim = NewEmail();

        for (var attempt = 1; attempt <= PermitLimit; attempt++)
        {
            using var allowed = await LoginWithDuplicateEmailAsync(client, NewEmail(), victim);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var rejected = await LoginWithDuplicateEmailAsync(client, NewEmail(), victim);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    /// <summary>The same proof with the duplicate in the other order: the decoy is last, so the decoy is throttled.</summary>
    [Fact]
    public async Task DuplicateEmailProperties_IgnoreTheFirstValue()
    {
        using var client = _factory.CreateClient();
        var victim = NewEmail();
        var decoy = NewEmail();

        for (var attempt = 1; attempt <= PermitLimit; attempt++)
        {
            using var allowed = await LoginWithDuplicateEmailAsync(client, victim, decoy);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var rejected = await LoginWithDuplicateEmailAsync(client, victim, decoy);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        // The first value never spent anything, because model binding and the limiter both used the last one.
        using var untouched = await LoginAsync(client, victim);
        Assert.Equal(HttpStatusCode.Unauthorized, untouched.StatusCode);
    }

    private static string NewEmail() => $"login.limit.{Guid.NewGuid():N}@example.com";

    /// <summary>Two addresses in different shards, so the isolation assertion cannot fail on a shard collision.</summary>
    private static (string Throttled, string Unaffected) NewEmailPair()
    {
        var throttled = NewEmail();
        var shard = ShardOf(throttled);

        while (true)
        {
            var unaffected = NewEmail();
            if (!string.Equals(ShardOf(unaffected), shard, StringComparison.Ordinal))
            {
                return (throttled, unaffected);
            }
        }
    }

    private static string ShardOf(string email) =>
        RateLimitEmailKey.Bucket(RateLimitEmailKey.Normalize(email)!);

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("api/Auth/login", new { email, password = "Wrong0!Pass" });

    private static Task<HttpResponseMessage> LoginWithDuplicateEmailAsync(
        HttpClient client,
        string first,
        string last) =>
        client.PostAsync(
            "api/Auth/login",
            new StringContent(
                $$"""{"email":"{{first}}","email":"{{last}}","password":"Wrong0!Pass"}""",
                Encoding.UTF8,
                "application/json"));
}

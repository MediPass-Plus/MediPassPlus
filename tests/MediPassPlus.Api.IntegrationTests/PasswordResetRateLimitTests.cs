using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MediPassPlus.Api.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// The password-reset budgets (FR-PRV-07, ADR-0007 decision 2, NFR-SEC-06). Both endpoints are rate-limited per
/// normalized email and client address: <c>forgot-password</c> because it emails a reset link for every active account
/// it names, and <c>reset-password</c> as the completion path of the anonymous reset flow. The answer to a caller under
/// the budget is the shared <c>202</c>/<c>400</c> the endpoints have always given, so the limiter never changes what an
/// honest caller sees, and the last attempt beyond the budget answers a <c>429</c> with <c>Retry-After</c> whose body
/// carries no account identifier, no token and no medical content.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PasswordResetRateLimitTests : IAsyncLifetime
{
    private const int RequestLimit = 3;
    private const int SubmitLimit = 5;
    private const string AngularOrigin = "http://localhost:4200";

    private readonly MediPassApiFactory _factory = new(
        nameof(PasswordResetRateLimitTests),
        "Testing",
        exposeApiReference: true,
        configureTestServices: services =>
        {
            services.Configure<PasswordResetRequestRateLimitOptions>(options =>
            {
                options.PermitLimit = RequestLimit;
                options.WindowMinutes = 15;
            });
            services.Configure<PasswordResetSubmitRateLimitOptions>(options =>
            {
                options.PermitLimit = SubmitLimit;
                options.WindowMinutes = 15;
            });
        });

    /// <summary>Nothing to prepare before the factory's host is used.</summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Drops this class's database and restores the environment variables the factory changed.</summary>
    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    /// <summary>
    /// The start of the flow answers <c>429</c> after the budget, and the budget is shared by every spelling of one
    /// address; another address from the same client is unaffected.
    /// </summary>
    [Fact]
    public async Task ForgotPassword_AttemptsAfterTheBudget_Return429WithRetryAfterAndNoEmailInTheBody()
    {
        using var client = _factory.CreateClient();
        var email = NewEmail();

        for (var attempt = 1; attempt <= RequestLimit; attempt++)
        {
            using var allowed = await ForgotAsync(client, email);
            Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
        }

        using var rejected = await ForgotAsync(client, email);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.True(int.Parse(retryAfter!.Single(), CultureInfo.InvariantCulture) > 0);

        var body = await rejected.Content.ReadAsStringAsync();
        Assert.DoesNotContain(email, body, StringComparison.OrdinalIgnoreCase);

        var problem = await rejected.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status429TooManyRequests, problem.Status);
        Assert.Equal("Too many password-reset requests.", problem.Title);
    }

    /// <summary>
    /// A rejected request made by the Angular client carries the exposed <c>Retry-After</c>, because that header is not
    /// CORS-safelisted and the browser client cannot read it otherwise (ADR-0007 decision 3, NFR-SEC-07).
    /// </summary>
    [Fact]
    public async Task ForgotPassword_RejectedForTheAngularOrigin_ExposesRetryAfter()
    {
        using var client = _factory.CreateClient();
        var email = NewEmail();

        for (var attempt = 1; attempt <= RequestLimit; attempt++)
        {
            using var allowed = await ForgotAsync(client, email);
            Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/forgot-password")
        {
            Content = JsonContent.Create(new { email })
        };
        request.Headers.Add("Origin", AngularOrigin);

        using var rejected = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(AngularOrigin, rejected.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains(
            "Retry-After",
            rejected.Headers.GetValues("Access-Control-Expose-Headers"),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ForgotPassword_SameAddressInAnotherCase_SharesOneBudget()
    {
        using var client = _factory.CreateClient();
        var email = NewEmail();

        for (var attempt = 1; attempt <= RequestLimit; attempt++)
        {
            using var allowed = await ForgotAsync(client, email);
            Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
        }

        using var rejected = await ForgotAsync(client, email.ToUpperInvariant());
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_FromTheSameClient_IsNotThrottledForAnotherEmail()
    {
        using var client = _factory.CreateClient();
        var (throttled, unaffected) = NewEmailPair();

        for (var attempt = 1; attempt <= RequestLimit; attempt++)
        {
            using var allowed = await ForgotAsync(client, throttled);
            Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
        }

        using var rejected = await ForgotAsync(client, throttled);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        using var other = await ForgotAsync(client, unaffected);
        Assert.Equal(HttpStatusCode.Accepted, other.StatusCode);
    }

    /// <summary>
    /// A body that repeats <c>email</c> spends the budget of the last value, which is the one MVC binds to the action.
    /// Before that matched, a caller could keep the action's address constant and rotate the decoy to get a fresh
    /// bucket per request, which defeated the limit entirely (FR-PRV-07).
    /// </summary>
    [Fact]
    public async Task ForgotPassword_DuplicateEmailProperties_SpendTheBudgetOfTheLastValue()
    {
        using var client = _factory.CreateClient();
        var victim = NewEmail();

        for (var attempt = 1; attempt <= RequestLimit; attempt++)
        {
            using var allowed = await ForgotWithDuplicateEmailAsync(client, NewEmail(), victim);
            Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
        }

        using var rejected = await ForgotWithDuplicateEmailAsync(client, NewEmail(), victim);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    /// <summary>The same proof with the duplicate in the other order: the decoy is last, so the decoy is throttled.</summary>
    [Fact]
    public async Task ForgotPassword_DuplicateEmailProperties_IgnoreTheFirstValue()
    {
        using var client = _factory.CreateClient();
        var victim = NewEmail();
        var decoy = NewEmail();

        for (var attempt = 1; attempt <= RequestLimit; attempt++)
        {
            using var allowed = await ForgotWithDuplicateEmailAsync(client, victim, decoy);
            Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
        }

        using var rejected = await ForgotWithDuplicateEmailAsync(client, victim, decoy);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        // The first value never spent anything, because model binding and the limiter both used the last one.
        using var untouched = await ForgotAsync(client, victim);
        Assert.Equal(HttpStatusCode.Accepted, untouched.StatusCode);
    }

    /// <summary>
    /// Completing a flow answers a <c>429</c> after the submit budget, while the shared bad-token <c>400</c> an honest
    /// caller gets is unchanged under the budget.
    /// </summary>
    [Fact]
    public async Task ResetPassword_AttemptsAfterTheBudget_Return429WithRetryAfterAndProblemDetails()
    {
        using var client = _factory.CreateClient();
        var email = NewEmail();

        for (var attempt = 1; attempt <= SubmitLimit; attempt++)
        {
            using var badToken = await ResetAsync(client, email, $"not-a-token-{attempt}");
            Assert.Equal(HttpStatusCode.BadRequest, badToken.StatusCode);
        }

        using var rejected = await ResetAsync(client, email, "not-a-token-over-the-budget");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.True(int.Parse(retryAfter!.Single(), CultureInfo.InvariantCulture) > 0);

        var body = await rejected.Content.ReadAsStringAsync();
        Assert.DoesNotContain(email, body, StringComparison.OrdinalIgnoreCase);

        var problem = await rejected.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status429TooManyRequests, problem.Status);
        Assert.Equal("Too many password-reset attempts.", problem.Title);
    }

    [Fact]
    public async Task ResetPassword_SameAddressInAnotherCase_SharesOneBudget()
    {
        using var client = _factory.CreateClient();
        var email = NewEmail();

        for (var attempt = 1; attempt <= SubmitLimit; attempt++)
        {
            using var badToken = await ResetAsync(client, email, $"not-a-token-{attempt}");
            Assert.Equal(HttpStatusCode.BadRequest, badToken.StatusCode);
        }

        using var rejected = await ResetAsync(client, email.ToUpperInvariant(), "not-a-token-over-the-budget");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_FromTheSameClient_IsNotThrottledForAnotherEmail()
    {
        using var client = _factory.CreateClient();
        var (throttled, unaffected) = NewEmailPair();

        for (var attempt = 1; attempt <= SubmitLimit; attempt++)
        {
            using var badToken = await ResetAsync(client, throttled, $"not-a-token-{attempt}");
            Assert.Equal(HttpStatusCode.BadRequest, badToken.StatusCode);
        }

        using var rejected = await ResetAsync(client, throttled, "not-a-token-over-the-budget");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        using var other = await ResetAsync(client, unaffected, "not-a-token");
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
    }

    /// <summary>The submit duplicate proof: the last <c>email</c> value is the one that spends the budget.</summary>
    [Fact]
    public async Task ResetPassword_DuplicateEmailProperties_SpendTheBudgetOfTheLastValue()
    {
        using var client = _factory.CreateClient();
        var victim = NewEmail();

        for (var attempt = 1; attempt <= SubmitLimit; attempt++)
        {
            using var allowed = await ResetWithDuplicateEmailAsync(client, NewEmail(), victim, $"not-a-token-{attempt}");
            Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
        }

        using var rejected = await ResetWithDuplicateEmailAsync(client, NewEmail(), victim, "not-a-token-over-the-budget");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    /// <summary>The same proof with the duplicate in the other order: the decoy is throttled, the victim is not.</summary>
    [Fact]
    public async Task ResetPassword_DuplicateEmailProperties_IgnoreTheFirstValue()
    {
        using var client = _factory.CreateClient();
        var victim = NewEmail();
        var decoy = NewEmail();

        for (var attempt = 1; attempt <= SubmitLimit; attempt++)
        {
            using var allowed = await ResetWithDuplicateEmailAsync(client, victim, decoy, $"not-a-token-{attempt}");
            Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
        }

        using var rejected = await ResetWithDuplicateEmailAsync(client, victim, decoy, "not-a-token-over-the-budget");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        using var untouched = await ResetAsync(client, victim, "not-a-token");
        Assert.Equal(HttpStatusCode.BadRequest, untouched.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_UnderBudget_AnswersTheBadToken400ForUnknownEmails()
    {
        using var client = _factory.CreateClient();

        using var response = await ResetAsync(client, NewEmail(), "not-a-real-reset-token");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string NewEmail() => $"password-reset.{Guid.NewGuid():N}@integration.medipass.test";

    /// <summary>
    /// Two addresses that land in different shards, so the isolation assertions cannot fail on a shard collision
    /// (two addresses in one shard intentionally share a budget).
    /// </summary>
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

    private static Task<HttpResponseMessage> ForgotAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/Auth/forgot-password", new { email });

    private static Task<HttpResponseMessage> ForgotWithDuplicateEmailAsync(
        HttpClient client,
        string first,
        string last) =>
        client.PostAsync(
            "/api/Auth/forgot-password",
            Json($$"""{"email":"{{first}}","email":"{{last}}"}"""));

    private static Task<HttpResponseMessage> ResetAsync(HttpClient client, string email, string token) =>
        client.PostAsJsonAsync(
            "/api/Auth/reset-password",
            new { email, token, newPassword = "Str0ng!Pass9" });

    private static Task<HttpResponseMessage> ResetWithDuplicateEmailAsync(
        HttpClient client,
        string first,
        string last,
        string token) =>
        client.PostAsync(
            "/api/Auth/reset-password",
            Json($$"""{"email":"{{first}}","email":"{{last}}","token":"{{token}}","newPassword":"Str0ng!Pass9"}"""));

    private static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");
}

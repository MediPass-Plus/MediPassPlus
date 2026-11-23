using System.Globalization;
using System.Net;
using System.Net.Http.Json;
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
        var throttled = NewEmail();
        var unaffected = NewEmail();

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
    public async Task ResetPassword_UnderBudget_AnswersTheBadToken400ForUnknownEmails()
    {
        using var client = _factory.CreateClient();

        using var response = await ResetAsync(client, NewEmail(), "not-a-real-reset-token");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string NewEmail() => $"password-reset.{Guid.NewGuid():N}@integration.medipass.test";

    private static Task<HttpResponseMessage> ForgotAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/Auth/forgot-password", new { email });

    private static Task<HttpResponseMessage> ResetAsync(HttpClient client, string email, string token) =>
        client.PostAsJsonAsync(
            "/api/Auth/reset-password",
            new { email, token, newPassword = "Str0ng!Pass9" });
}

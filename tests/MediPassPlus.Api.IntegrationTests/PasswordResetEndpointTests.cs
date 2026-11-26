using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediPassPlus.Api.Logging;
using MediPassPlus.Application.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP contract of the password-reset flow (FR-ACC-11, FR-PRV-08, NFR-SEC-06). Every case runs against the real
/// pipeline and the real Identity token provider; only the email transport is replaced, so a test can read the token
/// that actually reaches the user's mailbox and then prove what the API does and does not reveal.
/// </summary>
/// <remarks>
/// The reset token is the only proof of mailbox ownership, so it must travel by email alone. Its refusals are the
/// place an attacker probes for registered addresses, so every refusal must be byte-for-byte the same body.
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
public sealed class PasswordResetEndpointTests : IAsyncLifetime
{
    private const string Password = "Str0ng!Pass1";
    private const string NewPassword = "Str0ng!Pass2";
    private const string ThirdPassword = "Str0ng!Pass3";
    private const string WeakPassword = "weak";

    private readonly CapturingEmailSender _email = new();
    private readonly CapturingLoggerProvider _logs = new();
    private MediPassApiFactory _factory = null!;
    private HttpClient _client = null!;

    /// <summary>Starts the API in the test environment with the email transport and the log capture replaced.</summary>
    /// <returns>A completed task.</returns>
    public Task InitializeAsync()
    {
        _factory = new MediPassApiFactory(
            nameof(PasswordResetEndpointTests),
            "Testing",
            exposeApiReference: false,
            configureTestServices: services =>
            {
                services.AddSingleton<IEmailSender>(_email);
                services.AddSingleton<ILoggerProvider>(_logs);
            });

        _client = _factory.CreateClient();

        return Task.CompletedTask;
    }

    /// <summary>Disposes the HTTP client and the factory with its database.</summary>
    /// <returns>A task that completes when the host is stopped.</returns>
    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>The emailed token completes a reset and the old password stops working (FR-ACC-11).</summary>
    [Fact]
    public async Task ResetPassword_WithTheEmailedToken_ReplacesThePassword()
    {
        var email = await RegisterAsync("valid");
        var token = await RequestResetAsync(email);

        _logs.Clear();

        using var reset = await ResetAsync(email, token, NewPassword);

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, NewPassword)).StatusCode);

        // The completion is traceable without naming the account or repeating the token (FR-PRV-08).
        var entries = _logs.Entries;

        Assert.Contains(entries, entry => entry.Message.Contains("Password reset completed.", StringComparison.Ordinal));

        foreach (var entry in entries)
        {
            Assert.DoesNotContain(token, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(email, entry.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>A request without a token never reaches Identity and answers the validation envelope.</summary>
    [Fact]
    public async Task ResetPassword_WithAMissingToken_Returns400ValidationProblem()
    {
        var email = await RegisterAsync("missing-token");

        using var response = await ResetAsync(email, token: null, NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadProblemAsync(response);

        Assert.True(problem.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("Token", out _));
    }

    /// <summary>An unknown token answers the one generic body, with no error bag to explain itself.</summary>
    [Fact]
    public async Task ResetPassword_WithAnInvalidToken_ReturnsTheGeneric400()
    {
        var email = await RegisterAsync("invalid-token");

        using var response = await ResetAsync(email, "not-a-real-reset-token", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadProblemAsync(response);

        Assert.False(problem.TryGetProperty("errors", out _));
        Assert.Equal("The reset link is invalid or has expired.", problem.GetProperty("title").GetString());
        Assert.Equal(
            "Request a new password reset email and try again.",
            problem.GetProperty("detail").GetString());
    }

    /// <summary>A token resets a password once; replaying it is refused and changes nothing.</summary>
    [Fact]
    public async Task ResetPassword_WithAReusedToken_ReturnsTheGeneric400()
    {
        var email = await RegisterAsync("reused-token");
        var token = await RequestResetAsync(email);

        using var first = await ResetAsync(email, token, NewPassword);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var replay = await ResetAsync(email, token, ThirdPassword);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        var problem = await ReadProblemAsync(replay);

        Assert.False(problem.TryGetProperty("errors", out _));

        // The refused replay changed nothing: the password the first reset set still signs in, the third does not.
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, ThirdPassword)).StatusCode);
    }

    /// <summary>A new password below the policy is refused before any token is spent.</summary>
    [Fact]
    public async Task ResetPassword_WithAWeakPassword_Returns400ValidationProblem()
    {
        var email = await RegisterAsync("weak-password");
        var token = await RequestResetAsync(email);

        using var response = await ResetAsync(email, token, WeakPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadProblemAsync(response);

        Assert.True(problem.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("NewPassword", out _));

        // The token was not consumed by the refused request: the same link still works with a compliant password.
        using var retry = await ResetAsync(email, token, NewPassword);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    /// <summary>
    /// An unknown address and a known address with a bad token answer the same body, so the endpoint cannot be used to
    /// discover which addresses are registered (NFR-SEC-06).
    /// </summary>
    [Fact]
    public async Task ResetPassword_ForAnUnknownEmail_AnswersExactlyLikeABadToken()
    {
        var known = await RegisterAsync("enumeration");
        var unknown = NewEmail("enumeration-unknown");

        using var unknownResponse = await ResetAsync(unknown, "not-a-real-reset-token", NewPassword);
        using var knownResponse = await ResetAsync(known, "not-a-real-reset-token", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, unknownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, knownResponse.StatusCode);

        var unknownProblem = await ReadProblemAsync(unknownResponse);
        var knownProblem = await ReadProblemAsync(knownResponse);

        Assert.Equal(Shape(knownProblem), Shape(unknownProblem));
        Assert.False(unknownProblem.TryGetProperty("errors", out _));
        Assert.False(knownProblem.TryGetProperty("errors", out _));
    }

    /// <summary>Starting the flow answers the same body whether or not the address exists.</summary>
    [Fact]
    public async Task ForgotPassword_AnswersTheSameForAKnownAndAnUnknownEmail()
    {
        var known = await RegisterAsync("forgot-same");

        using var knownResponse = await _client.PostAsJsonAsync("/api/Auth/forgot-password", new { email = known });
        using var unknownResponse = await _client.PostAsJsonAsync(
            "/api/Auth/forgot-password",
            new { email = NewEmail("forgot-unknown") });

        Assert.Equal(HttpStatusCode.Accepted, knownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknownResponse.StatusCode);
        Assert.Equal(
            await knownResponse.Content.ReadAsStringAsync(),
            await unknownResponse.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A dead mail transport must not turn a registered address into a different answer. The <c>202</c> and its body
    /// stay identical to the unknown-address answer, nothing is captured, and the failure line repeats neither the
    /// address nor the token (FR-PRV-08, NFR-SEC-06).
    /// </summary>
    [Fact]
    public async Task ForgotPassword_WhenTheTransportFails_StillAnswersTheSame202()
    {
        var email = await RegisterAsync("smtp-down");

        _logs.Clear();
        _email.Clear();
        _email.Failure = new InvalidOperationException("The SMTP host did not answer.");

        try
        {
            using var failed = await _client.PostAsJsonAsync("/api/Auth/forgot-password", new { email });
            using var unknown = await _client.PostAsJsonAsync(
                "/api/Auth/forgot-password",
                new { email = NewEmail("smtp-down-unknown") });

            Assert.Equal(HttpStatusCode.Accepted, failed.StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
            Assert.Equal(
                await failed.Content.ReadAsStringAsync(),
                await unknown.Content.ReadAsStringAsync());

            // The transport refused the message, so the mailbox received nothing.
            Assert.Null(_email.Last);

            var entries = _logs.Entries;

            Assert.Contains(
                entries,
                entry => entry.Message.Contains(
                    "The password reset email could not be dispatched.",
                    StringComparison.Ordinal));

            foreach (var entry in entries)
            {
                Assert.DoesNotContain(email, entry.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            _email.Failure = null;
        }
    }

    /// <summary>
    /// The token reaches the mailbox and nowhere else: no line written while the request ran repeats it or the address
    /// it belongs to, and the only identifier is the synthetic correlation id (FR-PRV-08).
    /// </summary>
    [Fact]
    public async Task APasswordReset_NeverWritesTheTokenToTheLog()
    {
        var email = await RegisterAsync("reset-log");

        _logs.Clear();
        _email.Clear();

        const string correlationId = "password-reset-log-1";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/forgot-password")
        {
            Content = JsonContent.Create(new { email })
        };

        request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var message = _email.Last;
        Assert.NotNull(message);

        var token = message!.ResetToken;
        Assert.False(string.IsNullOrWhiteSpace(token));

        var entries = _logs.Entries;
        Assert.NotEmpty(entries);

        // The flow is traceable by the synthetic identifier alone, on every path of the request.
        var requested = entries
            .Where(entry => entry.Message.Contains("Password reset requested.", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(requested);
        Assert.All(requested, entry => Assert.Equal(correlationId, entry.CorrelationId));

        foreach (var entry in entries)
        {
            Assert.DoesNotContain(token, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(email, entry.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string NewEmail(string prefix) =>
        $"{prefix}.{Guid.NewGuid():N}@integration.medipass.test";

    private async Task<string> RegisterAsync(string prefix)
    {
        var email = NewEmail(prefix);

        using var response = await _client.PostAsJsonAsync(
            "/api/Auth/register",
            new { fullName = "Password Reset Probe", email, password = Password, role = "Patient" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return email;
    }

    private async Task<string> RequestResetAsync(string email)
    {
        _email.Clear();

        using var response = await _client.PostAsJsonAsync("/api/Auth/forgot-password", new { email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var message = _email.Last;

        Assert.NotNull(message);
        Assert.Equal(email, message!.To);

        return message.ResetToken;
    }

    private Task<HttpResponseMessage> ResetAsync(string email, string? token, string newPassword) =>
        _client.PostAsJsonAsync("/api/Auth/reset-password", new { email, token, newPassword });

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        _client.PostAsJsonAsync("/api/Auth/login", new { email, password });

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }

    /// <summary>
    /// Renders every member of a problem body except <c>traceId</c>, which is unique per request by design and so is
    /// the one value two requests can never share.
    /// </summary>
    /// <param name="problem">The parsed problem body.</param>
    /// <returns>The comparable shape of the body.</returns>
    private static string Shape(JsonElement problem) =>
        string.Join(
            "|",
            problem.EnumerateObject()
                .Where(member => member.Name != "traceId")
                .OrderBy(member => member.Name, StringComparer.Ordinal)
                .Select(member => $"{member.Name}={member.Value.GetRawText()}"));
}

using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Application.Email;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// Proves the reset token never leaves the API in an HTTP response outside local development (FR-ACC-11, NFR-SEC-06).
/// The host runs in the Production environment, so anything the Development path is allowed to expose is absent here:
/// the response carries the generic acknowledgement and nothing else, while the mailbox still receives the token.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PasswordResetProductionResponseTests : IAsyncLifetime
{
    private const string Password = "Str0ng!Pass1";
    private const string Acknowledgement = "If the email exists, a reset link has been sent.";

    private readonly CapturingEmailSender _email = new();
    private MediPassApiFactory _factory = null!;
    private HttpClient _client = null!;

    /// <summary>Starts the API in the Production environment with the email transport replaced.</summary>
    /// <returns>A completed task.</returns>
    public Task InitializeAsync()
    {
        _factory = new MediPassApiFactory(
            nameof(PasswordResetProductionResponseTests),
            "Production",
            exposeApiReference: false,
            configureTestServices: services => services.AddSingleton<IEmailSender>(_email));

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

    /// <summary>
    /// The acknowledgement is the whole body: no token, no reset link, no address echo. The email is the only channel
    /// that carries the token.
    /// </summary>
    [Fact]
    public async Task ForgotPassword_InProduction_NeverReturnsTheToken()
    {
        var email = $"production.{Guid.NewGuid():N}@integration.medipass.test";

        using (var register = await _client.PostAsJsonAsync(
                   "/api/Auth/register",
                   new { fullName = "Production Reset Probe", email, password = Password, role = "Patient" }))
        {
            Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        }

        _email.Clear();

        using var response = await _client.PostAsJsonAsync("/api/Auth/forgot-password", new { email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var message = _email.Last;

        Assert.NotNull(message);

        var token = message!.ResetToken;

        Assert.False(string.IsNullOrWhiteSpace(token));

        // The token really was produced and really did reach the mailbox.
        Assert.Equal(email, message.To);
        Assert.Contains(token, message.Body, StringComparison.Ordinal);

        // Nothing that identifies the reset travels back over HTTP.
        Assert.DoesNotContain(token, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Uri.EscapeDataString(token), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reset-password?", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(email, body, StringComparison.OrdinalIgnoreCase);

        // The answer is the fixed acknowledgement, so it cannot confirm which addresses are registered.
        Assert.Contains(Acknowledgement, body, StringComparison.Ordinal);
    }
}

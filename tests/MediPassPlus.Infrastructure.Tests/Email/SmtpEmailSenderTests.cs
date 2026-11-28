using System.Diagnostics;
using System.Net;
using MediPassPlus.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MediPassPlus.Infrastructure.Tests.Email;

/// <summary>
/// Drives the real <see cref="SmtpEmailSender"/> against a loopback SMTP listener. Every HTTP test replaces
/// <c>IEmailSender</c>, so this is the only place that proves the actual transport reads the configured host and port,
/// authenticates with the configured account, and sends the expected envelope and body (FR-ACC-11).
/// </summary>
public sealed class SmtpEmailSenderTests
{
    [Fact]
    public async Task SendAsync_HandsTheMessageToTheConfiguredServer()
    {
        await using var server = new FakeSmtpServer();
        var sender = CreateSender(server.Port, timeoutSeconds: 10);

        await sender.SendAsync(
            "patient@medipass.test",
            "Reset your MediPass+ password",
            "Reset token: abc123");

        var session = await server.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("sender@medipass.test", session.UserName);
        Assert.Equal("test-password", session.Password);

        Assert.NotNull(session.MailFrom);
        Assert.Contains("MAIL FROM:", session.MailFrom, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sender@medipass.test", session.MailFrom, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            session.Recipients,
            recipient => recipient.Contains("RCPT TO:", StringComparison.OrdinalIgnoreCase)
                && recipient.Contains("patient@medipass.test", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(session.Data);
        Assert.Contains("Subject: Reset your MediPass+ password", session.Data, StringComparison.Ordinal);
        Assert.Contains("Reset token: abc123", session.Data, StringComparison.Ordinal);
        Assert.Contains("MediPass+", session.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_GivesUpWhenTheServerNeverAnswers()
    {
        await using var server = new FakeSmtpServer(silent: true);
        var sender = CreateSender(server.Port, timeoutSeconds: 1);
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<Exception>(
            () => sender.SendAsync("patient@medipass.test", "Reset", "Body"));

        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(15),
            $"The send took {stopwatch.Elapsed}, so the configured one-second bound did not apply.");
    }

    private static SmtpEmailSender CreateSender(int port, int timeoutSeconds) =>
        new(
            Options.Create(new SmtpSettings
            {
                Host = IPAddress.Loopback.ToString(),
                Port = port,
                UseStartTls = false,
                UserName = "sender@medipass.test",
                Password = "test-password",
                FromAddress = "sender@medipass.test",
                FromName = "MediPass+",
                TimeoutSeconds = timeoutSeconds
            }),
            NullLogger<SmtpEmailSender>.Instance);
}

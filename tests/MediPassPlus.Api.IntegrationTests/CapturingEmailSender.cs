using System.Collections.Concurrent;
using MediPassPlus.Application.Email;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// A test-only <see cref="IEmailSender"/> that keeps the messages the API would have sent, so a test can read the
/// password-reset token that reaches the user's mailbox. It is registered through the test factory's hook and never
/// exists in the product host.
/// </summary>
internal sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<CapturedEmail> _sent = new();

    /// <summary>Every message sent since the last <see cref="Clear"/>, oldest first.</summary>
    public IReadOnlyList<CapturedEmail> Sent => _sent.ToArray();

    /// <summary>The most recent message, or <c>null</c> when none was sent.</summary>
    public CapturedEmail? Last => _sent.LastOrDefault();

    /// <summary>
    /// When set, every send throws it instead of capturing, so a test can prove the endpoint survives a dead mail
    /// transport. Reset it in a <c>finally</c> block: the same instance serves every request in that test, and xUnit
    /// creates a fresh test-class instance for the next test.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>Forgets every captured message so a test only reads what its own request produced.</summary>
    public void Clear()
    {
        while (_sent.TryDequeue(out _))
        {
        }
    }

    /// <inheritdoc />
    public Task SendAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        _sent.Enqueue(new CapturedEmail(to, subject, body));
        return Task.CompletedTask;
    }
}

/// <summary>One captured message: its envelope and the plain-text body the user would have received.</summary>
/// <param name="To">The recipient address.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="Body">The plain-text body.</param>
internal sealed record CapturedEmail(string To, string Subject, string Body)
{
    private const string TokenMarker = "enter this token on the reset page:";

    /// <summary>
    /// The raw reset token this message carries, read back from the body exactly as the user would see it. The body
    /// prints the token on the line after <see cref="TokenMarker"/>, unescaped, so the email link can stay untouched.
    /// </summary>
    public string ResetToken
    {
        get
        {
            var start = Body.IndexOf(TokenMarker, StringComparison.Ordinal);

            if (start < 0)
            {
                return string.Empty;
            }

            return Body[(start + TokenMarker.Length)..]
                .Split('\n')
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Length > 0) ?? string.Empty;
        }
    }
}

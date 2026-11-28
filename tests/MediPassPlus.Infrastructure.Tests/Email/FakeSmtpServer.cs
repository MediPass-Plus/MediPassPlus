using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MediPassPlus.Infrastructure.Tests.Email;

/// <summary>
/// A loopback SMTP listener for the real <c>SmtpEmailSender</c> test. It speaks just enough of the protocol to accept
/// one message, records the envelope and the DATA block, and can stay silent so the timeout test can prove the send
/// is bounded. It is a test double only: nothing in the product host knows about it.
/// </summary>
internal sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task<SmtpSession> _completion;
    private readonly bool _silent;

    /// <summary>Starts a listener on a free loopback port.</summary>
    /// <param name="silent">
    /// When <c>true</c>, accepts the connection but never sends the greeting, so the sender has to time out.
    /// </param>
    public FakeSmtpServer(bool silent = false)
    {
        _silent = silent;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _completion = Task.Run(ServeAsync);
    }

    /// <summary>The port the sender must connect to.</summary>
    public int Port { get; }

    /// <summary>The one session, complete after the client sends QUIT.</summary>
    public Task<SmtpSession> Completion => _completion;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        _listener.Stop();

        try
        {
            await _completion.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A cancelled or stopped listener is the expected end of a test helper.
        }

        _cancellation.Dispose();
    }

    private async Task<SmtpSession> ServeAsync()
    {
        using var client = await _listener.AcceptTcpClientAsync(_cancellation.Token);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n",
            AutoFlush = true
        };

        if (_silent)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, _cancellation.Token);
        }

        await writer.WriteLineAsync("220 localhost ESMTP MediPass+ test server");

        var session = new SmtpSession();

        while (!_cancellation.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(_cancellation.Token);

            if (line is null)
            {
                break;
            }

            if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("250-localhost");
                await writer.WriteLineAsync("250 AUTH LOGIN");
            }
            else if (line.StartsWith("AUTH LOGIN", StringComparison.OrdinalIgnoreCase))
            {
                // Some clients send the username as the initial response (AUTH LOGIN <base64>), others wait for the
                // 334 challenge. Accept both so the test does not depend on SmtpClient's exact form.
                var initialUserName = line["AUTH LOGIN".Length..].Trim();

                if (initialUserName.Length > 0)
                {
                    session.UserName = DecodeBase64(initialUserName);
                }
                else
                {
                    await writer.WriteLineAsync("334 VXNlcm5hbWU6");
                    session.UserName = DecodeBase64(await reader.ReadLineAsync(_cancellation.Token));
                }

                await writer.WriteLineAsync("334 UGFzc3dvcmQ6");
                session.Password = DecodeBase64(await reader.ReadLineAsync(_cancellation.Token));

                await writer.WriteLineAsync("235 2.7.0 Authentication successful");
            }
            else if (line.StartsWith("MAIL FROM:", StringComparison.OrdinalIgnoreCase))
            {
                session.MailFrom = line;
                await writer.WriteLineAsync("250 2.1.0 OK");
            }
            else if (line.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase))
            {
                session.Recipients.Add(line);
                await writer.WriteLineAsync("250 2.1.5 OK");
            }
            else if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");

                var data = new StringBuilder();

                while (true)
                {
                    var dataLine = await reader.ReadLineAsync(_cancellation.Token);

                    if (dataLine is null || dataLine == ".")
                    {
                        break;
                    }

                    data.Append(dataLine).Append("\r\n");
                }

                session.Data = data.ToString();
                await writer.WriteLineAsync("250 2.0.0 OK");
            }
            else if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("221 2.0.0 Bye");
                break;
            }
            else
            {
                await writer.WriteLineAsync("250 2.0.0 OK");
            }
        }

        return session;
    }

    private static string DecodeBase64(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        try
        {
            return Encoding.ASCII.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return value;
        }
    }
}

/// <summary>What the fake server received from one <c>SmtpEmailSender</c> call.</summary>
internal sealed class SmtpSession
{
    /// <summary>The <c>MAIL FROM</c> command line.</summary>
    public string? MailFrom { get; set; }

    /// <summary>The <c>RCPT TO</c> command lines.</summary>
    public List<string> Recipients { get; } = [];

    /// <summary>The DATA block, including the headers and the plain-text body.</summary>
    public string? Data { get; set; }

    /// <summary>The decoded <c>AUTH LOGIN</c> username.</summary>
    public string? UserName { get; set; }

    /// <summary>The decoded <c>AUTH LOGIN</c> password.</summary>
    public string? Password { get; set; }
}

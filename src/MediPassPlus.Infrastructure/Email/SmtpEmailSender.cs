using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text;
using MediPassPlus.Application.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediPassPlus.Infrastructure.Email;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpSettings> settings, ILogger<SmtpEmailSender> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(_settings.FromAddress, _settings.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(to);

        // SmtpClient's own default is 100 seconds, which would let a dead host hold a reset request for over a
        // minute. Bound both the client and the awaited send; the controller still answers the same 202 on failure.
        var timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = _settings.UseStartTls,
            Credentials = new NetworkCredential(_settings.UserName, _settings.Password),
            Timeout = (int)timeout.TotalMilliseconds
        };

        await client.SendMailAsync(message, timeoutSource.Token);

        // Identifiers only: the recipient and the body may contain a live reset token (AGENTS.md section 5).
        _logger.LogInformation("Transactional email dispatched over SMTP.");
    }
}

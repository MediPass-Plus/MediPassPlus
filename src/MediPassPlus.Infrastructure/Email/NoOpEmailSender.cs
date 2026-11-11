using System;
using System.Collections.Generic;
using System.Text;
using MediPassPlus.Application.Email;
using Microsoft.Extensions.Logging;

namespace MediPassPlus.Infrastructure.Email;

public sealed class NoOpEmailSender : IEmailSender
{
    private readonly ILogger<NoOpEmailSender> _logger;

    public NoOpEmailSender(ILogger<NoOpEmailSender> logger) => _logger = logger;

    public Task SendAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Smtp:Host is not configured; the transactional email was not sent.");
        return Task.CompletedTask;
    }
}

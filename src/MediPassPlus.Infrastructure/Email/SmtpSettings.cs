using System;
using System.Collections.Generic;
using System.Text;

namespace MediPassPlus.Infrastructure.Email;

/// <summary>
/// The password-reset SMTP transport (FR-ACC-11). A host-configured deployment must name a complete provider
/// account; an empty host is only a supported state in Development and Testing, where the no-op sender is registered.
/// </summary>
public sealed class SmtpSettings
{
    public const string SectionName = "Smtp";

    /// <summary>The provider's submission host. Empty selects the no-op sender in Development and Testing only.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>The submission port; 587 is the STARTTLS submission port.</summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// Maps to <see cref="System.Net.Mail.SmtpClient.EnableSsl"/>: <c>true</c> upgrades the connection with STARTTLS
    /// (submission port 587). Implicit TLS on port 465 is not supported by <see cref="System.Net.Mail.SmtpClient"/>.
    /// </summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>The provider account, normally the sending mailbox address.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>The provider password or app password; it is a secret and never belongs in appsettings*.json.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// The sender address. It deliberately has no default: a host-configured deployment must name an address its
    /// provider accepts, and a placeholder would pass validation and then be rejected on every send.
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>The display name on the sender address.</summary>
    public string FromName { get; set; } = "MediPass+";

    /// <summary>
    /// The upper bound, in seconds, for one SMTP send. The default is 10 instead of
    /// <see cref="System.Net.Mail.SmtpClient"/>'s 100-second default, so a dead host cannot hold a reset request.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;
}

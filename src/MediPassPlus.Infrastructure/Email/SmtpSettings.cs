using System;
using System.Collections.Generic;
using System.Text;

namespace MediPassPlus.Infrastructure.Email;

public sealed class SmtpSettings
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "no-reply@medipass.local";
    public string FromName { get; set; } = "MediPass+";
}

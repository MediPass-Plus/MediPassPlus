using System;
using System.Collections.Generic;
using System.Text;

namespace MediPassPlus.Application.Email;

public interface IEmailSender
{
    Task SendAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken = default);
}

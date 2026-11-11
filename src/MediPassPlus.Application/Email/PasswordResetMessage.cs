using System;
using System.Collections.Generic;
using System.Text;

namespace MediPassPlus.Application.Email;

public static class PasswordResetMessage
{
    /// <summary>Identity's default reset-token lifetime, quoted in the email so the user knows the deadline.</summary>
    public const int TokenLifetimeHours = 24;

    /// <summary>Builds the absolute URL the user clicks, with both query values escaped.</summary>
    public static string BuildLink(string baseUrl, string email, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return $"{baseUrl.TrimEnd('/')}/reset-password" +
               $"?email={Uri.EscapeDataString(email)}" +
               $"&token={Uri.EscapeDataString(token)}";
    }

    /// <summary>Builds the plain-text body. The email is the only channel that carries the token.</summary>
    public static string BuildBody(string link, string token) =>
        $"""
         Hello,

         We received a request to reset your MediPass+ password.
         Open this link to choose a new password (valid for {TokenLifetimeHours} hours):

         {link}

         If the link cannot be opened, enter this token on the reset page:
         {token}

         If you did not request this, you can ignore this email and your password stays unchanged.
         """;
}

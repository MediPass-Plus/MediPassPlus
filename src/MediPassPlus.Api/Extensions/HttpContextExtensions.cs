using Microsoft.AspNetCore.Http;

namespace MediPassPlus.Api.Extensions;

/// <summary>Helpers for values recorded on audit entries.</summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// Returns the connection IP address for an audit row, or <c>unknown</c> when it is missing or longer than the
    /// 45-character column (ADR-0004 decision 8).
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>The IP address to record.</returns>
    public static string GetAuditIpAddress(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ipAddress = context.Connection.RemoteIpAddress?.ToString();

        return string.IsNullOrWhiteSpace(ipAddress) || ipAddress.Length > 45
            ? "unknown"
            : ipAddress;
    }
}

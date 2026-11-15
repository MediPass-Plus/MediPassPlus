using MediPassPlus.Domain.VisitAccess;
using Microsoft.AspNetCore.Http;

namespace MediPassPlus.Api.Extensions;

/// <summary>Helpers for values recorded on audit entries and for the request's verified access grant.</summary>
public static class HttpContextExtensions
{
    private const string VerifiedAccessGrantKey = "MediPassPlus.VerifiedAccessGrant";

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

    /// <summary>
    /// Stores the grant a filter has just verified against the database, so the action never has to trust a claim or
    /// re-derive the scope from the request body (FR-PRV-01, ADR-0011).
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="grant">The verified grant.</param>
    public static void SetVerifiedAccessGrant(this HttpContext context, AccessGrant grant)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(grant);

        context.Items[VerifiedAccessGrantKey] = grant;
    }

    /// <summary>
    /// Returns the grant verified earlier in this request, or null when no grant filter ran.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>The verified grant, or null.</returns>
    public static AccessGrant? GetVerifiedAccessGrant(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items.TryGetValue(VerifiedAccessGrantKey, out var value)
            ? value as AccessGrant
            : null;
    }
}

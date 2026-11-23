using Microsoft.AspNetCore.Http;

namespace MediPassPlus.Api.RateLimiting;

/// <summary>
/// The single place that defines which rate-limit policies are keyed by the email inside the request body, and the
/// <see cref="HttpContext.Items"/> slot that carries the captured value from
/// <see cref="RateLimitEmailKeyMiddleware"/> to those policies (ADR-0007 decision 2, FR-PRV-07).
/// </summary>
/// <remarks>
/// The body is the only source of the email for an anonymous request, and the limiter runs before MVC model binding,
/// so the middleware has to read it first. One shared key keeps the three policies from inventing three spellings of
/// the same partition and from disagreeing about normalization.
/// </remarks>
internal static class RateLimitEmailKey
{
    /// <summary>The <see cref="HttpContext.Items"/> key that holds the normalized email for the current request.</summary>
    internal const string ItemKey = "MediPassPlus.RateLimit.Email";

    private static readonly string[] BodyKeyedPolicies =
    [
        LoginRateLimitPolicy.Name,
        PasswordResetRateLimitPolicy.RequestName,
        PasswordResetRateLimitPolicy.SubmitName,
    ];

    /// <summary>
    /// Reports whether the endpoint's policy needs the body email, so the middleware reads a body only where a policy
    /// actually uses it.
    /// </summary>
    /// <param name="policyName">The policy name from the endpoint's <c>EnableRateLimiting</c> metadata.</param>
    /// <returns><c>true</c> when the policy partitions by email.</returns>
    internal static bool IsBodyKeyed(string? policyName)
    {
        if (policyName is null)
        {
            return false;
        }

        foreach (var candidate in BodyKeyedPolicies)
        {
            if (string.Equals(candidate, policyName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Normalizes an email for a partition key. Identity compares addresses case-insensitively, so every limiter must
    /// too, or the same account could be reached through a second spelling of its address.
    /// </summary>
    /// <param name="email">The value read from the request body.</param>
    /// <returns>The normalized address, or <c>null</c> when the value is empty or whitespace.</returns>
    internal static string? Normalize(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    /// <summary>Reads the email the middleware captured for this request, if any.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <returns>The normalized address, or <c>null</c> when no usable email was in the body.</returns>
    internal static string? Read(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var captured) ? captured as string : null;
}

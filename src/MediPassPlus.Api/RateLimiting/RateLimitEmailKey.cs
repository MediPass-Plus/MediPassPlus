using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>
    /// The longest address that may key a partition. 254 characters is the RFC 5321 limit for a forward path, so a
    /// longer value is not an address Identity could ever match; it falls back to the client-address partition instead
    /// of becoming an arbitrarily long key.
    /// </summary>
    internal const int MaximumEmailLength = 254;

    /// <summary>
    /// The number of shards a normalized address is folded into. <c>PartitionedRateLimiter</c> caches one window per
    /// distinct key and never evicts it, so the address has to become a value from a fixed set: otherwise an anonymous
    /// caller sending one request per invented address grows the limiter's dictionary without limit (FR-PRV-07).
    /// A shard collision makes two addresses share one budget, which can only make a limit stricter — a caller can
    /// never escape the limiter through it. 16,384 shards cap the cached email windows at roughly 6 MB per client
    /// address, and a realistic number of accounts behind one address is far below the shard count.
    /// </summary>
    internal const int BucketCount = 16_384;

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
    /// <returns>
    /// The normalized address, or <c>null</c> when the value is empty, whitespace or longer than
    /// <see cref="MaximumEmailLength"/>.
    /// </returns>
    internal static string? Normalize(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalized = email.Trim().ToLowerInvariant();

        return normalized.Length > MaximumEmailLength ? null : normalized;
    }

    /// <summary>
    /// Folds a normalized address into one of <see cref="BucketCount"/> shards, so the limiter's key set stays bounded
    /// and the cached key never carries the address itself. The mapping is a stable SHA-256 prefix, so it does not
    /// change between restarts or replicas.
    /// </summary>
    /// <param name="normalizedEmail">A value returned by <see cref="Normalize"/>.</param>
    /// <returns>A short, non-reversible shard label such as <c>b1a2f</c>.</returns>
    internal static string Bucket(string normalizedEmail)
    {
        ArgumentException.ThrowIfNullOrEmpty(normalizedEmail);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail));
        var shard = BinaryPrimitives.ReadUInt32BigEndian(hash) % BucketCount;

        return string.Create(CultureInfo.InvariantCulture, $"b{shard:x4}");
    }

    /// <summary>Reads the email the middleware captured for this request, if any.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <returns>The normalized address, or <c>null</c> when no usable email was in the body.</returns>
    internal static string? Read(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var captured) ? captured as string : null;
}

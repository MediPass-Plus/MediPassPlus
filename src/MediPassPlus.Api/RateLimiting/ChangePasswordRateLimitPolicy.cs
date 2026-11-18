using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace MediPassPlus.Api.RateLimiting;

/// <summary>
/// Builds the per-account <c>ChangePassword</c> rate-limit partition and its rejection response. The account is the
/// partition, not the IP, so one caller cannot spend another account's budget and a shared NAT does not throttle every
/// user behind it (ADR-0007 decision 2, NFR-SEC-06).
/// </summary>
public static class ChangePasswordRateLimitPolicy
{
    /// <summary>The policy name used by <c>EnableRateLimiting</c>.</summary>
    public const string Name = "ChangePassword";

    private const string UserPartitionPrefix = "user:";
    private const string IpPartitionPrefix = "ip:";

    /// <summary>
    /// Creates the fixed-window partition for the authenticated caller. The endpoint is <c>[Authorize]</c>d, so the
    /// claim is present; the IP fallback only keeps an anonymous request from sharing one bucket before authorization
    /// refuses it.
    /// </summary>
    /// <param name="context">The current HTTP request, including the authenticated principal.</param>
    /// <param name="options">The configured permit count and window length.</param>
    /// <returns>The partition the partitioned limiter caches for this key.</returns>
    public static RateLimitPartition<string> CreatePartition(
        HttpContext context,
        ChangePasswordRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var partitionKey = !string.IsNullOrWhiteSpace(userId)
            ? string.Concat(UserPartitionPrefix, userId)
            : string.Concat(IpPartitionPrefix, context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.PermitLimit,
            Window = TimeSpan.FromMinutes(options.WindowMinutes),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    /// <summary>
    /// Writes the 429 response: <c>Retry-After</c> plus the shared ProblemDetails envelope. The body carries no
    /// password, no account identifier and no medical content (ADR-0007 decision 3, ADR-0010).
    /// </summary>
    /// <param name="httpContext">The request being rejected.</param>
    /// <param name="lease">The failed lease, which carries the retry delay when the limiter provides one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static async ValueTask WriteRejectionAsync(
        HttpContext httpContext,
        RateLimitLease lease,
        CancellationToken cancellationToken)
    {
        await RateLimitProblem.WriteAsync(
            httpContext,
            lease,
            "Too many password-change attempts.",
            cancellationToken);
    }
}

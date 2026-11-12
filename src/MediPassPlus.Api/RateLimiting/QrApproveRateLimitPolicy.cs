using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace MediPassPlus.Api.RateLimiting;

/// <summary>
/// Builds the per-patient <c>QrApprove</c> rate-limit partition and its rejection response. The policy is named once
/// and reused by every endpoint that answers a request, so the later scan-token and code paths do not add a second
/// limiter (ADR-0007 decisions 2 and 4, FR-PRV-07).
/// </summary>
public static class QrApproveRateLimitPolicy
{
    /// <summary>The policy name used by <c>RequireRateLimiting</c> and <c>EnableRateLimiting</c>.</summary>
    public const string Name = "QrApprove";

    private const string PatientPartitionPrefix = "patient:";
    private const string IpPartitionPrefix = "ip:";

    /// <summary>
    /// Creates the fixed-window partition for the authenticated patient. An unauthenticated caller falls back to the
    /// remote IP so anonymous traffic cannot share one bucket; authorization refuses those requests before the action
    /// runs anyway.
    /// </summary>
    /// <param name="context">The current HTTP request, including the authenticated principal.</param>
    /// <param name="options">The configured permit count and window length.</param>
    /// <returns>The partition the partitioned limiter caches for this key.</returns>
    public static RateLimitPartition<string> CreatePartition(
        HttpContext context,
        QrApproveRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var patientId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var partitionKey = !string.IsNullOrWhiteSpace(patientId)
            ? string.Concat(PatientPartitionPrefix, patientId)
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
    /// Writes the 429 response: <c>Retry-After</c> plus the shared ProblemDetails envelope. The body contains no
    /// medical content and no identifier that is not already part of the request scope (ADR-0007 decision 3,
    /// ADR-0010).
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
            "Too many access-request decisions.",
            cancellationToken);
    }
}

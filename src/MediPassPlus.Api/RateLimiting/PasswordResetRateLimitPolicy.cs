using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace MediPassPlus.Api.RateLimiting;

/// <summary>
/// Builds the two password-reset partitions and their rejection responses (FR-PRV-07, ADR-0007 decision 2). The
/// partition is the email address together with the client address, exactly like <see cref="LoginRateLimitPolicy"/>,
/// so one caller cannot spend another account's budget and a shared network does not throttle unrelated users. The two
/// endpoints keep separate budgets: starting the flow sends an email and mints a token, while completing it only
/// spends a token, so they are different kinds of work and ADR-0007 gives each policy group its own numbers.
/// </summary>
/// <remarks>
/// The email is captured by <see cref="RateLimitEmailKeyMiddleware"/> before the limiter runs, because the limiter
/// executes before MVC model binding. A request whose body did not yield an email falls back to the client address
/// alone, so a malformed body cannot escape the limiter.
/// </remarks>
public static class PasswordResetRateLimitPolicy
{
    /// <summary>The policy name for starting the password-reset flow.</summary>
    public const string RequestName = "PasswordResetRequest";

    /// <summary>The policy name for completing the password-reset flow.</summary>
    public const string SubmitName = "PasswordResetSubmit";

    private const string RequestPartitionPrefix = "reset-request:";
    private const string SubmitPartitionPrefix = "reset-submit:";
    private const string EmailPartitionPrefix = "email:";
    private const string IpPartitionPrefix = "ip:";
    private const string UnknownPartitionValue = "unknown";

    /// <summary>Creates the fixed-window partition for starting the reset flow.</summary>
    /// <param name="context">The current HTTP request, including the captured email when one was available.</param>
    /// <param name="options">The configured permit count and window length.</param>
    /// <returns>The partition the partitioned limiter caches for this key.</returns>
    public static RateLimitPartition<string> CreateRequestPartition(
        HttpContext context,
        PasswordResetRequestRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        return RateLimitPartition.GetFixedWindowLimiter(
            CreatePartitionKey(context, RequestPartitionPrefix),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.PermitLimit,
                Window = TimeSpan.FromMinutes(options.WindowMinutes),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    }

    /// <summary>Creates the fixed-window partition for completing the reset flow.</summary>
    /// <param name="context">The current HTTP request, including the captured email when one was available.</param>
    /// <param name="options">The configured permit count and window length.</param>
    /// <returns>The partition the partitioned limiter caches for this key.</returns>
    public static RateLimitPartition<string> CreateSubmitPartition(
        HttpContext context,
        PasswordResetSubmitRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        return RateLimitPartition.GetFixedWindowLimiter(
            CreatePartitionKey(context, SubmitPartitionPrefix),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.PermitLimit,
                Window = TimeSpan.FromMinutes(options.WindowMinutes),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    }

    /// <summary>
    /// Writes the start policy's <c>429</c> response: <c>Retry-After</c> plus the shared ProblemDetails envelope. The
    /// body carries no email, no account identifier and no medical content (ADR-0007 decision 3, ADR-0010).
    /// </summary>
    /// <param name="httpContext">The request being rejected.</param>
    /// <param name="lease">The failed lease, which carries the retry delay when the limiter provides one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static ValueTask WriteRequestRejectionAsync(
        HttpContext httpContext,
        RateLimitLease lease,
        CancellationToken cancellationToken) =>
        RateLimitProblem.WriteAsync(httpContext, lease, "Too many password-reset requests.", cancellationToken);

    /// <summary>
    /// Writes the completion policy's <c>429</c> response: <c>Retry-After</c> plus the shared ProblemDetails envelope.
    /// The body carries no token, no password, no account identifier and no medical content (ADR-0007 decision 3,
    /// ADR-0010).
    /// </summary>
    /// <param name="httpContext">The request being rejected.</param>
    /// <param name="lease">The failed lease, which carries the retry delay when the limiter provides one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static ValueTask WriteSubmitRejectionAsync(
        HttpContext httpContext,
        RateLimitLease lease,
        CancellationToken cancellationToken) =>
        RateLimitProblem.WriteAsync(httpContext, lease, "Too many password-reset attempts.", cancellationToken);

    private static string CreatePartitionKey(HttpContext context, string policyPrefix)
    {
        var email = RateLimitEmailKey.Read(context);
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? UnknownPartitionValue;

        return string.IsNullOrWhiteSpace(email)
            ? string.Concat(policyPrefix, IpPartitionPrefix, ip)
            : string.Concat(policyPrefix, EmailPartitionPrefix, email, ":", IpPartitionPrefix, ip);
    }
}

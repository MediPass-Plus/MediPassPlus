using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace MediPassPlus.Api.RateLimiting;

/// <summary>
/// Builds the <c>Login</c> rate-limit partition and its rejection response. The partition is the email address
/// together with the client address, as ADR-0007 decision 2 requires: one caller cannot spend another account's
/// budget, a shared network does not throttle unrelated users, and an unknown address is still bounded by the client
/// address. The email is captured by <see cref="LoginRateLimitKeyMiddleware"/> before the limiter runs, because the
/// limiter executes before MVC model binding.
/// </summary>
public static class LoginRateLimitPolicy
{
    /// <summary>The policy name used by <c>EnableRateLimiting</c>.</summary>
    public const string Name = "Login";

    /// <summary>The <see cref="HttpContext.Items"/> key that holds the normalized email for the current request.</summary>
    internal const string EmailItemKey = "MediPassPlus.Login.Email";

    private const string EmailPartitionPrefix = "email:";
    private const string IpPartitionPrefix = "ip:";
    private const string UnknownPartitionValue = "unknown";

    /// <summary>
    /// Creates the fixed-window partition for the request. A request whose body did not yield an email falls back to
    /// the client address alone, so a malformed body cannot escape the limiter.
    /// </summary>
    /// <param name="context">The current HTTP request, including the captured email when one was available.</param>
    /// <param name="options">The configured permit count and window length.</param>
    /// <returns>The partition the partitioned limiter caches for this key.</returns>
    public static RateLimitPartition<string> CreatePartition(
        HttpContext context,
        LoginRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var email = context.Items.TryGetValue(EmailItemKey, out var captured) ? captured as string : null;
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? UnknownPartitionValue;

        var partitionKey = string.IsNullOrWhiteSpace(email)
            ? string.Concat(IpPartitionPrefix, ip)
            : string.Concat(EmailPartitionPrefix, email, ":", IpPartitionPrefix, ip);

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.PermitLimit,
            Window = TimeSpan.FromMinutes(options.WindowMinutes),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    /// <summary>
    /// Normalizes an email for the partition key. Identity compares addresses case-insensitively, so the limiter must
    /// too, or the same account could be guessed through a second spelling of its address.
    /// </summary>
    /// <param name="email">The value read from the request body.</param>
    /// <returns>The normalized address, or <c>null</c> when the value is empty or whitespace.</returns>
    internal static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    /// <summary>
    /// Writes the 429 response: <c>Retry-After</c> plus the shared ProblemDetails envelope. The body carries no email,
    /// no account identifier and no medical content (ADR-0007 decision 3, ADR-0010).
    /// </summary>
    /// <param name="httpContext">The request being rejected.</param>
    /// <param name="lease">The failed lease, which carries the retry delay when the limiter provides one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public static ValueTask WriteRejectionAsync(
        HttpContext httpContext,
        RateLimitLease lease,
        CancellationToken cancellationToken) =>
        RateLimitProblem.WriteAsync(httpContext, lease, "Too many login attempts.", cancellationToken);
}

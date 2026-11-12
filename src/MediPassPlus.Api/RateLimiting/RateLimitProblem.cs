using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace MediPassPlus.Api.RateLimiting;

/// <summary>
/// Writes the shared <c>429</c> response: <c>Retry-After</c> from the failed lease plus the ProblemDetails envelope.
/// Every rate-limit policy uses this writer so the response contract cannot drift (ADR-0007 decision 3, ADR-0010).
/// </summary>
internal static class RateLimitProblem
{
    private const string RetryAfterFallbackSeconds = "60";
    private const string ProblemType = "https://tools.ietf.org/html/rfc6585#section-4";

    /// <summary>Writes the rejection response with the supplied title.</summary>
    /// <param name="httpContext">The request being rejected.</param>
    /// <param name="lease">The failed lease, which carries the retry delay when the limiter provides one.</param>
    /// <param name="title">The client-facing title for this policy family.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the response is written.</returns>
    public static async ValueTask WriteAsync(
        HttpContext httpContext,
        RateLimitLease lease,
        string title,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(lease);

        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            httpContext.Response.Headers[HeaderNames.RetryAfter] = Math
                .Ceiling(retryAfter.TotalSeconds)
                .ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            // A limiter without RetryAfter metadata must still honour the response contract.
            httpContext.Response.Headers[HeaderNames.RetryAfter] = RetryAfterFallbackSeconds;
        }

        await Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: title,
                detail: "Wait for the delay in the Retry-After header, then try again.",
                type: ProblemType)
            .ExecuteAsync(httpContext);
    }
}

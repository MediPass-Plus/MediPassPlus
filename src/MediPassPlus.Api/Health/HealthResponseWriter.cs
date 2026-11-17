using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediPassPlus.Api.Health;

/// <summary>
/// Writes the <c>GET /health</c> body: the overall status and one status per check, and nothing else
/// (NFR-REL-06, FR-PRV-08).
/// </summary>
/// <remarks>
/// The <see cref="HealthReport"/> itself is never serialized. Its entries carry a description and, on failure, the
/// exception, and both can hold a connection string, a server name or medical text. The writer projects only the
/// status names, so a new check cannot leak detail by accident.
/// </remarks>
public static class HealthResponseWriter
{
    /// <summary>Writes the report with <c>200</c> when every check is healthy and <c>503</c> otherwise.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="report">The health report produced by the registered checks.</param>
    /// <returns>A task that completes when the body is written.</returns>
    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.StatusCode = report.Status == HealthStatus.Healthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable;

        // A probe result is never cacheable: a cached "Healthy" would outlive the failure it is meant to reveal.
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentType = "application/json; charset=utf-8";

        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToDictionary(entry => entry.Key, entry => entry.Value.Status.ToString()),
            totalDurationMs = (long)report.TotalDuration.TotalMilliseconds
        };

        await JsonSerializer.SerializeAsync(context.Response.Body, body, cancellationToken: context.RequestAborted);
    }
}

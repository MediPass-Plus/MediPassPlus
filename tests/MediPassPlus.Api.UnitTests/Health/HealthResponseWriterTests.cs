using System.Text;
using System.Text.Json;
using MediPassPlus.Api.Health;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediPassPlus.Api.UnitTests.Health;

/// <summary>
/// Proves the <c>/health</c> body is the statuses only. A description or an exception in the report must never reach
/// the response, because the endpoint is anonymous (NFR-REL-06, FR-PRV-08).
/// </summary>
public sealed class HealthResponseWriterTests
{
    [Fact]
    public async Task WriteAsync_HealthyReport_Returns200WithBothStatuses()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                [HealthCheckNames.Api] = Entry(HealthStatus.Healthy, "api ok"),
                [HealthCheckNames.Database] = Entry(HealthStatus.Healthy, "db ok")
            },
            TimeSpan.FromMilliseconds(7));

        var (context, raw) = await WriteAsync(report);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("application/json; charset=utf-8", context.Response.ContentType);

        using var document = JsonDocument.Parse(raw);
        var body = document.RootElement;
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty(HealthCheckNames.Api).GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty(HealthCheckNames.Database).GetString());

        // Statuses only: a duration would be a timing signal the contract does not promise.
        Assert.False(body.TryGetProperty("totalDurationMs", out _));
    }

    [Fact]
    public async Task WriteAsync_UnhealthyReport_Returns503()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                [HealthCheckNames.Api] = Entry(HealthStatus.Healthy, "api ok"),
                [HealthCheckNames.Database] = Entry(HealthStatus.Unhealthy, "db down")
            },
            TimeSpan.FromMilliseconds(3));

        var (context, raw) = await WriteAsync(report);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);

        using var document = JsonDocument.Parse(raw);
        Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "Unhealthy",
            document.RootElement.GetProperty("checks").GetProperty(HealthCheckNames.Database).GetString());
    }

    [Fact]
    public async Task WriteAsync_DegradedReport_Returns503SoTheProbeActs()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                [HealthCheckNames.Api] = Entry(HealthStatus.Healthy, "api ok"),
                [HealthCheckNames.Database] = Entry(HealthStatus.Degraded, "db slow")
            },
            TimeSpan.FromMilliseconds(3));

        var (context, raw) = await WriteAsync(report);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);

        using var document = JsonDocument.Parse(raw);
        Assert.Equal("Degraded", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "Degraded",
            document.RootElement.GetProperty("checks").GetProperty(HealthCheckNames.Database).GetString());
    }

    [Fact]
    public async Task WriteAsync_FailureWithAnException_WritesNeitherTheDescriptionNorTheException()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                [HealthCheckNames.Database] = new HealthReportEntry(
                    HealthStatus.Unhealthy,
                    "Server=db.internal;Password=hunter2",
                    TimeSpan.FromMilliseconds(1),
                    new InvalidOperationException("Server=db.internal;Password=hunter2"),
                    null)
            },
            TimeSpan.FromMilliseconds(1));

        var (context, raw) = await WriteAsync(report);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.DoesNotContain("db.internal", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", raw, StringComparison.Ordinal);
    }

    private static HealthReportEntry Entry(HealthStatus status, string description) =>
        new(status, description, TimeSpan.FromMilliseconds(1), null, null);

    private static async Task<(DefaultHttpContext Context, string Raw)> WriteAsync(HealthReport report)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await HealthResponseWriter.WriteAsync(context, report);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        return (context, await reader.ReadToEndAsync());
    }
}

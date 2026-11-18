using MediPassPlus.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediPassPlus.Api.Health;

/// <summary>
/// Reports whether the API can open a connection to its database (NFR-REL-06). It calls
/// <c>Database.CanConnectAsync</c>, which already turns a connection failure into <c>false</c>, so the check never has
/// to expose a provider exception.
/// </summary>
/// <remarks>
/// The description is always fixed text. A connection string, a server name or a <c>SqlException</c> message in the
/// <c>/health</c> body would be a leak: the endpoint is anonymous by design (FR-PRV-08, NFR-SEC-08).
/// </remarks>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly MediPassDbContext _db;

    /// <summary>Creates the check over the request's scoped database context.</summary>
    /// <param name="db">The application database context.</param>
    public DatabaseHealthCheck(MediPassDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("Database reachable.")
                : HealthCheckResult.Unhealthy("Database unreachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Fixed text on purpose: the exception may carry the connection string or the server name.
            return HealthCheckResult.Unhealthy("Database check failed.");
        }
    }
}

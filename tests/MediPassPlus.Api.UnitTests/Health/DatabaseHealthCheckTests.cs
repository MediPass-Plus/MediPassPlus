using MediPassPlus.Api.Health;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediPassPlus.Api.UnitTests.Health;

/// <summary>
/// Proves the database health check reports reachability and never carries a connection string or a provider
/// exception into its description (NFR-REL-06, FR-PRV-08).
/// </summary>
public sealed class DatabaseHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ReachableDatabase_ReturnsHealthy()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new MediPassDbContext(options);

        var result = await new DatabaseHealthCheck(db).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_UnreachableDatabase_ReturnsUnhealthyWithoutLeakingTheConnection()
    {
        const string connectionString =
            "Server=127.0.0.1,1;Database=no_such_database;User Id=nobody;Password=not-a-real-password;" +
            "Connect Timeout=1;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var db = new MediPassDbContext(options);

        var result = await new DatabaseHealthCheck(db).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Description);
        Assert.DoesNotContain("127.0.0.1", result.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-real-password", result.Description, StringComparison.Ordinal);
        Assert.Null(result.Exception);
    }
}

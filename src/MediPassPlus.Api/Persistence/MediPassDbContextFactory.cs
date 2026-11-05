using MediPassPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MediPassPlus.Api.Persistence;

/// <summary>
/// Creates <see cref="MediPassDbContext"/> instances for Entity Framework Core design-time operations
/// without starting the API host.
/// </summary>
public sealed class MediPassDbContextFactory : IDesignTimeDbContextFactory<MediPassDbContext>
{
    /// <summary>
    /// Builds a context using the SQL Server connection string from the API configuration sources.
    /// </summary>
    /// <param name="args">Arguments provided by the Entity Framework Core tools.</param>
    /// <returns>A configured <see cref="MediPassDbContext"/> instance.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>ConnectionStrings:DefaultConnection</c> is not configured.
    /// </exception>
    public MediPassDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddUserSecrets<MediPassDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection must be configured for EF Core design-time operations.");

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new MediPassDbContext(options);
    }
}

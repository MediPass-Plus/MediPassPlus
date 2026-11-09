using System.Collections.Concurrent;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Extensions;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// Hosts the real API over HTTP with a SQL Server database (ADR-0006).
/// Testcontainers SQL Server is the default. Set <c>MEDIPASS_TEST_SQLSERVER</c> to a SQL Server
/// connection string to run locally without Docker; the provider stays SQL Server, so authorization
/// and Identity behavior are exercised through the same code path as production.
/// </summary>
public sealed class MediPassApiFactory : WebApplicationFactory<Program>
{
    private const string JwtIssuer = "MediPassPlus";
    private const string JwtAudience = "MediPassPlus.Client";
    private const string JwtSecretKey = "integration-tests-only-key-0123456789abcdef";
    private const string TestPassword = "Integration1!";
    private const string LocalSqlServerVariable = "MEDIPASS_TEST_SQLSERVER";

    private readonly string _connectionString;
    private readonly string _databaseName;
    private readonly MsSqlContainer? _sqlContainer;
    private readonly Dictionary<string, string?> _previousEnvironment = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _tokens = new(StringComparer.Ordinal);
    private bool _cleanedUp;

    public MediPassApiFactory()
    {
        var serverConnectionString = Environment.GetEnvironmentVariable(LocalSqlServerVariable);

        if (string.IsNullOrWhiteSpace(serverConnectionString))
        {
            _sqlContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            _sqlContainer.StartAsync().GetAwaiter().GetResult();
            serverConnectionString = _sqlContainer.GetConnectionString();
        }

        _databaseName = $"MediPassPlus_IntegrationTests_{Guid.NewGuid():N}";
        _connectionString = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = _databaseName
        }.ConnectionString;

        // Program reads Jwt and ConnectionStrings before builder.Build(), so the deferred
        // WebApplicationFactory configuration would arrive too late. Environment variables are
        // part of WebApplication.CreateBuilder's configuration from the start.
        SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        SetEnvironmentVariable("Jwt__Issuer", JwtIssuer);
        SetEnvironmentVariable("Jwt__Audience", JwtAudience);
        SetEnvironmentVariable("Jwt__SecretKey", JwtSecretKey);
        SetEnvironmentVariable("Jwt__AccessTokenMinutes", "15");
        SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _connectionString);
        SetEnvironmentVariable("Logging__LogLevel__Microsoft.EntityFrameworkCore", "Warning");

        ApplyMigrations();
    }

    /// <summary>
    /// Creates (or reuses) a test user in the requested role and returns a signed JWT for it.
    /// Tokens are minted by the application's own <see cref="JwtTokenService"/>, so the test client
    /// exercises the real bearer authentication and role checks.
    /// </summary>
    public async Task<string> CreateTokenForRoleAsync(string role)
    {
        if (_tokens.TryGetValue(role, out var existingToken))
        {
            return existingToken;
        }

        using var scope = Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var jwt = scope.ServiceProvider.GetRequiredService<JwtTokenService>();

        if (!await roleManager.RoleExistsAsync(role))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create role '{role}': {string.Join("; ", roleResult.Errors.Select(e => e.Description))}");
            }
        }

        var email = $"{role.ToLowerInvariant()}@integration.medipass.test";
        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = $"{role} Integration Test",
                Role = role,
                IsActive = true,
                EmailConfirmed = true
            };

            var userResult = await userManager.CreateAsync(user, TestPassword);
            if (!userResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create '{email}': {string.Join("; ", userResult.Errors.Select(e => e.Description))}");
            }

            var roleAssignment = await userManager.AddToRoleAsync(user, role);
            if (!roleAssignment.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not add '{email}' to '{role}': {string.Join("; ", roleAssignment.Errors.Select(e => e.Description))}");
            }
        }

        var token = jwt.CreateAccessToken(user, new[] { role });
        _tokens[role] = token;
        return token;
    }

    /// <summary>Disposes the host, drops the test database and stops the Testcontainers database.</summary>
    public async ValueTask CleanupAsync()
    {
        if (_cleanedUp)
        {
            return;
        }

        _cleanedUp = true;
        await base.DisposeAsync();
        DropDatabase();
        RestoreEnvironmentVariables();

        if (_sqlContainer is not null)
        {
            await _sqlContainer.DisposeAsync();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await CleanupAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The API's Development seeder must not run in tests; default roles are seeded by Program
        // for every environment, and the test factory creates only the users each test needs.
        builder.UseEnvironment("Testing");
    }

    private void ApplyMigrations()
    {
        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        using var db = new MediPassDbContext(options);
        db.Database.Migrate();
    }

    private void SetEnvironmentVariable(string name, string value)
    {
        _previousEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    private void RestoreEnvironmentVariables()
    {
        foreach (var (name, value) in _previousEnvironment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        _previousEnvironment.Clear();
    }

    private void DropDatabase()
    {
        SqlConnection.ClearAllPools();

        var masterConnectionString = new SqlConnectionStringBuilder(_connectionString)
        {
            InitialCatalog = "master"
        }.ConnectionString;

        using var connection = new SqlConnection(masterConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{_databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{_databaseName}];
            END
            """;
        command.ExecuteNonQuery();
    }
}

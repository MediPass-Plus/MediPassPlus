using System;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// Resolves the SQL Server instance that hosts every test class's database. Testcontainers SQL Server is the
/// default (ADR-0006 decision 5); set <c>MEDIPASS_TEST_SQLSERVER</c> to an existing SQL Server to run without
/// Docker. The container is started once per test process and is reaped by Testcontainers' resource reaper when
/// the process exits, while each <see cref="MediPassApiFactory"/> creates and drops its own catalog.
/// </summary>
internal static class TestSqlServer
{
    private const string LocalSqlServerVariable = "MEDIPASS_TEST_SQLSERVER";

    private static readonly Lazy<string> ServerConnectionString =
        new(Resolve, LazyThreadSafetyMode.ExecutionAndPublication);

    private static MsSqlContainer? _container;

    /// <summary>The server-level connection string; every test database is created on this instance.</summary>
    public static string ConnectionString => ServerConnectionString.Value;

    private static string Resolve()
    {
        var localSqlServer = Environment.GetEnvironmentVariable(LocalSqlServerVariable);

        if (!string.IsNullOrWhiteSpace(localSqlServer))
        {
            return new SqlConnectionStringBuilder(localSqlServer)
            {
                InitialCatalog = "master"
            }.ConnectionString;
        }

        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        _container.StartAsync().GetAwaiter().GetResult();

        return new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "master"
        }.ConnectionString;
    }

    /// <summary>Creates a connection to the server's <c>master</c> database, which the caller disposes.</summary>
    /// <returns>A closed connection to the server.</returns>
    public static DbConnection CreateServerConnection() => new SqlConnection(ConnectionString);
}

using MediPassPlus.Domain.Clinical;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// The hard, order-independent proof that the harness is per-class: two live factory instances, two catalogs on the
/// same server, and a row written through one context that the other context cannot see. It fails if the harness ever
/// hands two test classes one database (ADR-0006 decision 3).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class DatabaseIsolationTests
{
    private const string FirstMarker = "isolation-proof:first";
    private const string SecondMarker = "isolation-proof:second";

    /// <summary>Two class fixtures own distinct catalogs and cannot read each other's rows.</summary>
    [Fact]
    public async Task TwoClassFixtures_UseDistinctCatalogs_AndCannotReadEachOthersRows()
    {
        await using var first = new MediPassApiFactory(nameof(DatabaseIsolationTests) + "First");
        await using var second = new MediPassApiFactory(nameof(DatabaseIsolationTests) + "Second");

        Assert.NotEqual(first.DatabaseName, second.DatabaseName);
        Assert.NotEqual(first.ConnectionString, second.ConnectionString);
        Assert.Equal(
            first.DatabaseName,
            new SqlConnectionStringBuilder(first.ConnectionString).InitialCatalog);
        Assert.Equal(
            second.DatabaseName,
            new SqlConnectionStringBuilder(second.ConnectionString).InitialCatalog);

        await SeedMarkerAsync(first, FirstMarker);
        await SeedMarkerAsync(second, SecondMarker);

        Assert.True(await ContainsMarkerAsync(first, FirstMarker));
        Assert.False(await ContainsMarkerAsync(first, SecondMarker));
        Assert.True(await ContainsMarkerAsync(second, SecondMarker));
        Assert.False(await ContainsMarkerAsync(second, FirstMarker));

        Assert.True(await CatalogExistsAsync(first.DatabaseName));
        Assert.True(await CatalogExistsAsync(second.DatabaseName));
    }

    private static async Task SeedMarkerAsync(MediPassApiFactory factory, string marker)
    {
        await using var db = factory.CreateDbContext();
        db.Clinics.Add(new Clinic { Name = marker, Address = "Isolation proof" });
        await db.SaveChangesAsync();
    }

    private static async Task<bool> ContainsMarkerAsync(MediPassApiFactory factory, string marker)
    {
        await using var db = factory.CreateDbContext();
        return await db.Clinics.AnyAsync(clinic => clinic.Name == marker);
    }

    private static async Task<bool> CatalogExistsAsync(string databaseName)
    {
        await using var connection = TestSqlServer.CreateServerConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = databaseName;
        command.Parameters.Add(parameter);

        return (int)(await command.ExecuteScalarAsync())! > 0;
    }
}

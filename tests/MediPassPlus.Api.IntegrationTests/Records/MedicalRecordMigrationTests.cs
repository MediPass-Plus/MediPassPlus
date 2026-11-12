using System.Globalization;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediPassPlus.Api.IntegrationTests.Records;

/// <summary>
/// Applies the real migrations to a scratch SQL Server database and then asserts what MP-28 promises: the four
/// <c>records</c> tables, their required and restricted foreign keys, the unique record-per-patient index and the
/// backfill that gives an existing patient one record and an existing doctor none (FR-REC-01, FR-REC-09, ADR-0005).
/// <para>
/// This executes the migration SQL instead of comparing script text, so a wrong foreign key, a failing index or a
/// broken <c>INSERT ... SELECT</c> is attributed to this ticket.
/// </para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class MedicalRecordMigrationTests : IAsyncLifetime
{
    private const string RecordMigrationSuffix = "_AddMedicalRecordSchema";

    private static readonly string[] RecordTables =
    [
        "records.Diagnoses",
        "records.MedicalRecords",
        "records.Prescriptions",
        "records.Reports"
    ];

    private static readonly string[] RequiredColumns =
    [
        "AppointmentId",
        "DiagnosisId",
        "MedicalRecordId",
        "UploadedByUserId"
    ];

    private static readonly string[] OptionalColumns =
    [
        "SupersedesDiagnosisId",
        "SupersedesPrescriptionId"
    ];

    private readonly MediPassApiFactory _factory;
    private readonly string _databaseName = $"MediPassPlus_RecordMigration_{Guid.NewGuid():N}";

    private string _connectionString = string.Empty;

    /// <summary>Initializes the test class with the shared API factory.</summary>
    /// <param name="factory">The collection fixture that hosts the API and database.</param>
    public MedicalRecordMigrationTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Points this test at its own scratch catalog on the shared test server.</summary>
    public Task InitializeAsync()
    {
        _connectionString = new SqlConnectionStringBuilder(_factory.ConnectionString)
        {
            InitialCatalog = _databaseName
        }.ConnectionString;

        return Task.CompletedTask;
    }

    /// <summary>Drops the scratch catalog so the run leaves nothing behind.</summary>
    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();

        var masterConnectionString = new SqlConnectionStringBuilder(_connectionString)
        {
            InitialCatalog = "master"
        }.ConnectionString;

        await using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{_databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{_databaseName}];
            END
            """;
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task The_migration_creates_the_records_schema_and_backfills_patients_only()
    {
        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        await using var db = new MediPassDbContext(options);
        var migrator = db.Database.GetService<IMigrator>();

        var migrations = db.Database.GetMigrations().ToList();
        var recordMigrationIndex = migrations.FindIndex(id =>
            id.EndsWith(RecordMigrationSuffix, StringComparison.Ordinal));

        Assert.True(recordMigrationIndex > 0, "AddMedicalRecordSchema must follow an earlier migration.");

        // Step 1: reach the state the Record module shipped in - accounts exist, the records schema does not.
        await migrator.MigrateAsync(migrations[recordMigrationIndex - 1]);

        var patient = NewUser(DefaultRoles.Patient);
        var doctor = NewUser(DefaultRoles.Doctor);
        db.Users.AddRange(patient, doctor);
        await db.SaveChangesAsync();

        var patientRole = new IdentityRole<Guid>(DefaultRoles.Patient) { NormalizedName = "PATIENT" };
        var doctorRole = new IdentityRole<Guid>(DefaultRoles.Doctor) { NormalizedName = "DOCTOR" };
        db.Roles.AddRange(patientRole, doctorRole);
        await db.SaveChangesAsync();

        db.UserRoles.AddRange(
            new IdentityUserRole<Guid> { UserId = patient.Id, RoleId = patientRole.Id },
            new IdentityUserRole<Guid> { UserId = doctor.Id, RoleId = doctorRole.Id });
        await db.SaveChangesAsync();

        // Step 2: apply the record migration exactly as a deployment would, backfill included.
        await migrator.MigrateAsync();

        // Step 3: the backfill gives the pre-existing patient one record and leaves the doctor without one.
        Assert.Equal(1, await db.MedicalRecords.CountAsync(row => row.PatientId == patient.Id));
        Assert.Equal(0, await db.MedicalRecords.CountAsync(row => row.PatientId == doctor.Id));

        // Step 4: the four tables exist, and nothing else was created in the records schema.
        var tables = await QueryAsync(
            """
            SELECT s.name + '.' + t.name
            FROM sys.tables AS t
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE s.name = N'records'
            """);

        Assert.Equal(
            RecordTables.OrderBy(name => name, StringComparer.Ordinal),
            tables.Select(row => row[0]).OrderBy(name => name, StringComparer.Ordinal));

        // Step 5: every record foreign key refuses delete, so no cascade path exists.
        var foreignKeys = await QueryAsync(
            """
            SELECT fk.delete_referential_action_desc, c.name
            FROM sys.foreign_keys AS fk
            INNER JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
            INNER JOIN sys.columns AS c
                ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
            INNER JOIN sys.tables AS t ON t.object_id = fk.parent_object_id
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE s.name = N'records'
            """);

        Assert.NotEmpty(foreignKeys);
        Assert.All(foreignKeys, row => Assert.Equal("NO_ACTION", row[0]));

        var foreignKeyColumns = foreignKeys.Select(row => row[1]).ToHashSet(StringComparer.Ordinal);
        Assert.Subset(foreignKeyColumns, RequiredColumns.ToHashSet(StringComparer.Ordinal));
        Assert.Subset(foreignKeyColumns, OptionalColumns.ToHashSet(StringComparer.Ordinal));

        // Step 6: the owning links are NOT NULL; only the correction links may be null.
        var columns = await QueryAsync(
            """
            SELECT c.name, CONVERT(nvarchar(10), c.is_nullable)
            FROM sys.columns AS c
            INNER JOIN sys.tables AS t ON t.object_id = c.object_id
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE s.name = N'records'
            """);

        foreach (var column in RequiredColumns)
        {
            Assert.Contains(columns, row => row[0] == column && row[1] == "0");
        }

        foreach (var column in OptionalColumns)
        {
            Assert.Contains(columns, row => row[0] == column && row[1] == "1");
        }

        // Step 7: one record per patient is enforced by the database, not only by the application.
        var uniqueIndexes = await QueryAsync(
            """
            SELECT i.name, CONVERT(nvarchar(10), i.is_unique)
            FROM sys.indexes AS i
            INNER JOIN sys.tables AS t ON t.object_id = i.object_id
            INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
            WHERE s.name = N'records' AND i.name IS NOT NULL
            """);

        Assert.Contains(uniqueIndexes, row => row[0] == "IX_MedicalRecords_PatientId" && row[1] == "1");
    }

    private static ApplicationUser NewUser(string role)
    {
        var email = $"{role.ToLowerInvariant()}.{Guid.NewGuid():N}@integration.medipass.test";

        return new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = $"{role} Migration Test",
            Role = role,
            EmailConfirmed = true,
            IsActive = true
        };
    }

    private async Task<List<string[]>> QueryAsync(string sql)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<string[]>();
        while (await reader.ReadAsync())
        {
            var values = new string[reader.FieldCount];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = reader.IsDBNull(index)
                    ? string.Empty
                    : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty;
            }

            rows.Add(values);
        }

        return rows;
    }
}

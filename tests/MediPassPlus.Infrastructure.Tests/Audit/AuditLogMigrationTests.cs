using MediPassPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediPassPlus.Infrastructure.Tests.Audit;

public class AuditLogMigrationTests
{
    [Fact]
    public void Migration_script_creates_the_audit_table_and_patient_index()
    {
        // Generating the script does not connect; the database name is only a placeholder.
        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlServer(
                "Server=.;Database=MediPassPlusMigrationScript;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        using var db = new MediPassDbContext(options);
        var migrator = db.Database.GetService<IMigrator>();

        var script = migrator.GenerateScript();

        Assert.Contains("[audit].[AuditLogs]", script);
        Assert.Contains("IX_AuditLogs_PatientId_OccurredAtUtc", script);
    }
}

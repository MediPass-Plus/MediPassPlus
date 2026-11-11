using MediPassPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MediPassPlus.Infrastructure.Tests.Records;

public class RecordMigrationTests
{
    [Fact]
    public void Migration_script_creates_the_record_tables_and_indexes()
    {
        // Generating the script does not connect; the database name is only a placeholder.
        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlServer(
                "Server=.;Database=MediPassPlusMigrationScript;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        using var db = new MediPassDbContext(options);
        var migrator = db.Database.GetService<IMigrator>();

        var script = migrator.GenerateScript();

        Assert.Contains("[records].[MedicalRecords]", script);
        Assert.Contains("[records].[Diagnoses]", script);
        Assert.Contains("[records].[Prescriptions]", script);
        Assert.Contains("[records].[Reports]", script);

        // One record per patient (FR-REC-01) and the newest-first history page (FR-REC-02).
        Assert.Contains("IX_MedicalRecords_PatientId", script);
        Assert.Contains("IX_Diagnoses_MedicalRecordId_DiagnosedAtUtc", script);

        // A diagnosis or a prescription can be corrected once, never twice (FR-REC-09, ADR-0005).
        Assert.Contains("IX_Diagnoses_SupersedesDiagnosisId", script);
        Assert.Contains("IX_Prescriptions_SupersedesPrescriptionId", script);
    }
}

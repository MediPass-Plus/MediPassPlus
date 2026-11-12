using MediPassPlus.Domain.Records;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Tests.Records;

public class RecordModelTests
{
    private static MediPassDbContext CreateContext()
    {
        // The provider is required to build the relational model; this database is never opened.
        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlServer(
                "Server=.;Database=MediPassPlusModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new MediPassDbContext(options);
    }

    [Theory]
    [InlineData(typeof(MedicalRecord), "MedicalRecords")]
    [InlineData(typeof(Diagnosis), "Diagnoses")]
    [InlineData(typeof(Prescription), "Prescriptions")]
    [InlineData(typeof(Report), "Reports")]
    public void Every_record_entity_is_mapped_to_the_records_schema(Type entityType, string tableName)
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(entityType);

        Assert.NotNull(entity);
        Assert.Equal(tableName, entity.GetTableName());
        Assert.Equal("records", entity.GetSchema());
    }

    [Fact]
    public void A_patient_can_own_only_one_record()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(MedicalRecord))!;

        var index = entity.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(MedicalRecord.PatientId) }));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Diagnosis_links_are_required_and_restricted()
    {
        AssertAllRestricted(typeof(Diagnosis));
        AssertRequiredForeignKey(typeof(Diagnosis), nameof(Diagnosis.MedicalRecordId));
        AssertRequiredForeignKey(typeof(Diagnosis), nameof(Diagnosis.AppointmentId));
    }

    [Fact]
    public void Prescription_links_are_required_and_restricted()
    {
        AssertAllRestricted(typeof(Prescription));
        AssertRequiredForeignKey(typeof(Prescription), nameof(Prescription.DiagnosisId));
    }

    [Fact]
    public void Report_links_are_required_and_restricted()
    {
        AssertAllRestricted(typeof(Report));
        AssertRequiredForeignKey(typeof(Report), nameof(Report.MedicalRecordId));
        AssertRequiredForeignKey(typeof(Report), nameof(Report.UploadedByUserId));
    }

    [Fact]
    public void Record_links_are_restricted()
    {
        AssertAllRestricted(typeof(MedicalRecord));
        AssertRequiredForeignKey(typeof(MedicalRecord), nameof(MedicalRecord.PatientId));
    }


    [Fact]
    public void History_index_orders_diagnoses_per_record()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(Diagnosis))!;

        var index = entity.GetIndexes().SingleOrDefault(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(Diagnosis.MedicalRecordId), nameof(Diagnosis.DiagnosedAtUtc) }));

        Assert.NotNull(index);
    }

    [Fact]
    public void A_diagnosis_and_a_prescription_can_each_be_superseded_once()
    {
        using var db = CreateContext();

        AssertCorrectionIndex<Diagnosis>(db, nameof(Diagnosis.SupersedesDiagnosisId));
        AssertCorrectionIndex<Prescription>(db, nameof(Prescription.SupersedesPrescriptionId));
    }

    [Fact]
    public void Clinical_text_columns_hold_unbounded_ciphertext()
    {
        using var db = CreateContext();

        var diagnosis = db.Model.FindEntityType(typeof(Diagnosis))!;
        var description = diagnosis.FindProperty(nameof(Diagnosis.Description))!;
        Assert.Equal("nvarchar(max)", description.GetColumnType());
        Assert.False(description.IsNullable);

        var prescription = db.Model.FindEntityType(typeof(Prescription))!;
        foreach (var name in new[] { nameof(Prescription.MedicineName), nameof(Prescription.Dosage) })
        {
            var property = prescription.FindProperty(name)!;
            Assert.Equal("nvarchar(max)", property.GetColumnType());
            Assert.False(property.IsNullable);
        }

        // Notes are optional, but still ciphertext and still unbounded.
        var notes = prescription.FindProperty(nameof(Prescription.Notes))!;
        Assert.Equal("nvarchar(max)", notes.GetColumnType());
        Assert.True(notes.IsNullable);
    }

    private static void AssertAllRestricted(Type entityType)
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(entityType)!;

        // No cascade delete anywhere: a record, a diagnosis or a prescription can never take its children with it
        // (FR-REC-09, ADR-0005).
        Assert.All(entity.GetForeignKeys(), foreignKey =>
            Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
    }

    private static void AssertRequiredForeignKey(Type entityType, string propertyName)
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(entityType)!;

        var foreignKey = entity.GetForeignKeys().Single(foreignKey =>
            foreignKey.Properties.Select(property => property.Name).SequenceEqual(new[] { propertyName }));

        Assert.True(foreignKey.IsRequired);
    }

    private static void AssertCorrectionIndex<TEntity>(DbContext db, string propertyName)
    {
        var entity = db.Model.FindEntityType(typeof(TEntity))!;

        var index = entity.GetIndexes().SingleOrDefault(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { propertyName }));

        Assert.NotNull(index);
        Assert.True(index.IsUnique);
        Assert.NotNull(index.GetFilter());
    }
}

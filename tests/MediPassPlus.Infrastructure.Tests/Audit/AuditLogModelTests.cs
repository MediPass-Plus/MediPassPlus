using MediPassPlus.Domain.Audit;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Tests.Audit;

public class AuditLogModelTests
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

    [Fact]
    public void AuditLog_is_mapped_to_the_audit_schema()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(AuditLog));

        Assert.NotNull(entity);
        Assert.Equal("AuditLogs", entity.GetTableName());
        Assert.Equal("audit", entity.GetSchema());
    }

    [Fact]
    public void Action_and_entity_type_are_stored_as_strings()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(AuditLog))!;

        var action = entity.FindProperty(nameof(AuditLog.Action))!;
        var entityType = entity.FindProperty(nameof(AuditLog.EntityType))!;

        Assert.Equal("nvarchar(40)", action.GetColumnType());
        Assert.Equal("nvarchar(40)", entityType.GetColumnType());
    }

    [Fact]
    public void Patient_timeline_index_exists()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(AuditLog))!;

        var index = entity.GetIndexes().SingleOrDefault(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(AuditLog.PatientId), nameof(AuditLog.OccurredAtUtc) }));

        Assert.NotNull(index);
    }

    [Fact]
    public void Every_foreign_key_restricts_deletes()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(AuditLog))!;

        Assert.All(entity.GetForeignKeys(), foreignKey =>
            Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
    }
}

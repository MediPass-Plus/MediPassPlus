using MediPassPlus.Domain.Audit;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Audit;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Tests.Audit;

public class AuditLogImmutabilityInterceptorTests
{
    private static MediPassDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseInMemoryDatabase($"audit-interceptor-{Guid.NewGuid():N}")
            .AddInterceptors(new AuditLogImmutabilityInterceptor())
            .Options;

        return new MediPassDbContext(options);
    }

    private static AuditLog NewAuditLog() => AuditLog.Create(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        AuditAction.RecordRead,
        AuditEntityType.MedicalRecord,
        "record-1",
        "203.0.113.10",
        new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Insert_is_allowed()
    {
        await using var db = CreateContext();
        db.AuditLogs.Add(NewAuditLog());

        await db.SaveChangesAsync();

        Assert.Equal(1, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Update_is_rejected()
    {
        await using var db = CreateContext();
        var log = NewAuditLog();
        db.AuditLogs.Add(log);
        await db.SaveChangesAsync();

        db.Entry(log).Property(nameof(AuditLog.IpAddress)).CurrentValue = "198.51.100.7";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("append-only", exception.Message);
    }

    [Fact]
    public async Task Delete_is_rejected()
    {
        await using var db = CreateContext();
        var log = NewAuditLog();
        db.AuditLogs.Add(log);
        await db.SaveChangesAsync();

        db.AuditLogs.Remove(log);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("append-only", exception.Message);
    }

    [Fact]
    public void Synchronous_update_is_rejected()
    {
        using var db = CreateContext();
        var log = NewAuditLog();
        db.AuditLogs.Add(log);
        db.SaveChanges();

        db.Entry(log).Property(nameof(AuditLog.IpAddress)).CurrentValue = "198.51.100.7";

        var exception = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("append-only", exception.Message);
    }
}

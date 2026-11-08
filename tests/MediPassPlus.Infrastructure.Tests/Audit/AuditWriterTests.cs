using MediPassPlus.Application.Audit;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Audit;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Tests.Audit;

public class AuditWriterTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static MediPassDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseInMemoryDatabase($"audit-writer-{Guid.NewGuid():N}")
            .AddInterceptors(new AuditLogImmutabilityInterceptor())
            .Options;

        return new MediPassDbContext(options);
    }

    private static AuditEntry NewEntry() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        AuditAction.DiagnosisCreated,
        AuditEntityType.Diagnosis,
        "diagnosis-42",
        "203.0.113.10",
        Guid.Parse("33333333-3333-3333-3333-333333333333"));

    [Fact]
    public async Task Add_stages_without_saving()
    {
        await using var db = CreateContext();
        var writer = new AuditWriter(db, new FixedTimeProvider(Now));

        var log = writer.Add(NewEntry());

        Assert.Equal(EntityState.Added, db.Entry(log).State);
        Assert.Equal(0, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task WriteAsync_saves_and_maps_fields()
    {
        await using var db = CreateContext();
        var writer = new AuditWriter(db, new FixedTimeProvider(Now));
        var entry = NewEntry();

        var log = await writer.WriteAsync(entry);

        var saved = await db.AuditLogs.SingleAsync();
        Assert.Equal(log.Id, saved.Id);
        Assert.Equal(entry.UserId, saved.UserId);
        Assert.Equal(entry.PatientId, saved.PatientId);
        Assert.Equal(entry.Action, saved.Action);
        Assert.Equal(entry.EntityType, saved.EntityType);
        Assert.Equal(entry.EntityId, saved.EntityId);
        Assert.Equal(entry.IpAddress, saved.IpAddress);
        Assert.Equal(entry.AccessGrantId, saved.AccessGrantId);
        Assert.Equal(Now, saved.OccurredAtUtc);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

using MediPassPlus.Application.Audit;
using MediPassPlus.Domain.Audit;

namespace MediPassPlus.Application.Tests.Audit;

/// <summary>
/// The patient audit read model: every stored field is mapped, the page metadata describes the requested page, the
/// paging values reach the store already capped, and the service cannot audit its own read (FR-PRV-03, NFR-PRF-04,
/// ADR-0004 decision 4).
/// </summary>
public class AuditLogQueryServiceTests
{
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FirstAuditId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SecondAuditId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid GrantId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetForPatientAsync_MapsEveryFieldAndThePageMetadata()
    {
        var reader = new FakeAuditLogReader
        {
            Page = new AuditLogPage(
                new[]
                {
                    new AuditLogRow(
                        FirstAuditId,
                        DoctorUserId,
                        AuditAction.RecordRead,
                        AuditEntityType.MedicalRecord,
                        "record-1",
                        Now,
                        "203.0.113.10",
                        GrantId)
                },
                TotalCount: 21)
        };
        var service = new AuditLogQueryService(reader);

        var result = await service.GetForPatientAsync(
            PatientId,
            new MyAuditQuery { Page = 2, PageSize = 10 });

        Assert.Equal(PatientId, reader.PatientUserId);
        Assert.Equal(10, reader.Skip);
        Assert.Equal(10, reader.Take);

        Assert.Equal(21, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);

        var item = Assert.Single(result.Items);
        Assert.Equal(FirstAuditId, item.Id);
        Assert.Equal(DoctorUserId, item.UserId);
        Assert.Equal("RecordRead", item.Action);
        Assert.Equal("MedicalRecord", item.EntityType);
        Assert.Equal("record-1", item.EntityId);
        Assert.Equal(Now, item.OccurredAtUtc);
        Assert.Equal("203.0.113.10", item.IpAddress);
        Assert.Equal(GrantId, item.AccessGrantId);
    }

    [Fact]
    public async Task GetForPatientAsync_KeepsTheStoreOrderAndAnEmptyPage()
    {
        var reader = new FakeAuditLogReader
        {
            Page = new AuditLogPage(
                new[]
                {
                    NewRow(FirstAuditId, Now),
                    NewRow(SecondAuditId, Now.AddMinutes(-5))
                },
                TotalCount: 2)
        };
        var service = new AuditLogQueryService(reader);

        var result = await service.GetForPatientAsync(PatientId, new MyAuditQuery());

        Assert.Equal(new[] { FirstAuditId, SecondAuditId }, result.Items.Select(item => item.Id));

        reader.Page = new AuditLogPage(Array.Empty<AuditLogRow>(), TotalCount: 0);
        var empty = await service.GetForPatientAsync(PatientId, new MyAuditQuery());

        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalCount);
        Assert.Equal(1, empty.Page);
        Assert.Equal(20, empty.PageSize);
    }

    [Fact]
    public async Task GetForPatientAsync_CapsThePageSizeAtTheMaximum()
    {
        var reader = new FakeAuditLogReader();
        var service = new AuditLogQueryService(reader);

        // A caller bypassing the validator cannot pull more than the maximum in one page.
        await service.GetForPatientAsync(PatientId, new MyAuditQuery { Page = 1, PageSize = 500 });

        Assert.Equal(50, reader.Take);
        Assert.Equal(0, reader.Skip);
    }

    [Fact]
    public void TheService_CannotAuditItsOwnRead()
    {
        var dependencies = typeof(AuditLogQueryService)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToList();

        Assert.DoesNotContain(typeof(IAuditWriter), dependencies);
    }

    private static AuditLogRow NewRow(Guid id, DateTimeOffset occurredAtUtc) =>
        new(id,
            DoctorUserId,
            AuditAction.RecordRead,
            AuditEntityType.MedicalRecord,
            "record-1",
            occurredAtUtc,
            "203.0.113.10",
            null);

    private sealed class FakeAuditLogReader : IAuditLogReader
    {
        public AuditLogPage Page { get; set; } = new(Array.Empty<AuditLogRow>(), 0);

        public Guid PatientUserId { get; private set; }

        public int Skip { get; private set; }

        public int Take { get; private set; }

        public Task<AuditLogPage> ReadForPatientAsync(
            Guid patientUserId,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            PatientUserId = patientUserId;
            Skip = skip;
            Take = take;

            return Task.FromResult(Page);
        }
    }
}

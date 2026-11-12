using MediPassPlus.Application.DoctorSchedule;
using MediPassPlus.Application.Tests.AppointmentStatusFlow;
using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.Tests.DoctorSchedule;

/// <summary>
/// The doctor schedule resolves the doctor from the authenticated user and converts the requested clinic-local date
/// into a UTC range before reading (FR-BKG-07, US-10).
/// </summary>
public class DoctorScheduleServiceTests
{
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly Date = new(2026, 11, 11);

    [Fact]
    public async Task GetAsync_WhenTheDoctorExists_ReturnsTheRowsOrderedByTheStore()
    {
        var rows = new List<DoctorScheduleRow>
        {
            new(10, new DateTime(2026, 11, 11, 7, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 11, 7, 30, 0, DateTimeKind.Utc), "First Patient", AppointmentStatus.Booked),
            new(11, new DateTime(2026, 11, 11, 7, 30, 0, DateTimeKind.Utc), new DateTime(2026, 11, 11, 8, 0, 0, DateTimeKind.Utc), "Second Patient", AppointmentStatus.CheckedIn)
        };
        var store = new FakeDoctorScheduleStore { Doctor = NewDoctor(), Rows = rows };
        var service = new DoctorScheduleService(store, new FakeClinicTimeZone());

        var result = await service.GetAsync(DoctorUserId, Date);

        Assert.True(result.IsSuccess);
        var items = Assert.IsAssignableFrom<IReadOnlyList<DoctorScheduleItemDto>>(result.Items);
        Assert.Equal(2, items.Count);
        Assert.Equal(10, items[0].AppointmentId);
        Assert.Equal("First Patient", items[0].PatientName);
        Assert.Equal(nameof(AppointmentStatus.Booked), items[0].Status);
        Assert.Equal(11, items[1].AppointmentId);
        Assert.Equal(nameof(AppointmentStatus.CheckedIn), items[1].Status);

        Assert.True(store.WasListed);
        Assert.Equal(7, store.LastDoctorId);
        Assert.Equal(new DateTime(2026, 11, 10, 22, 0, 0, DateTimeKind.Utc), store.LastFromUtc);
        Assert.Equal(new DateTime(2026, 11, 11, 22, 0, 0, DateTimeKind.Utc), store.LastToUtc);
    }

    [Fact]
    public async Task GetAsync_WhenTheUserHasNoDoctorProfile_ReturnsDoctorNotFoundWithoutReading()
    {
        var store = new FakeDoctorScheduleStore { Doctor = null };
        var service = new DoctorScheduleService(store, new FakeClinicTimeZone());

        var result = await service.GetAsync(DoctorUserId, Date);

        Assert.False(result.IsSuccess);
        Assert.Equal(DoctorScheduleFailure.DoctorNotFound, result.Failure);
        Assert.False(store.WasListed);
    }

    private static Doctor NewDoctor() =>
        new()
        {
            Id = 7,
            UserId = DoctorUserId,
            Specialty = "General",
            ClinicId = 1
        };
}

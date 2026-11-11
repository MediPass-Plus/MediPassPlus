using MediPassPlus.Application.AppointmentStatusFlow;
using MediPassPlus.Application.Tests.VisitAccess;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.StaffAdmin;

namespace MediPassPlus.Application.Tests.AppointmentStatusFlow;

/// <summary>
/// The staff check-in and doctor completion rules: every refusal reason, the UTC timestamp, and the mapping from a
/// lost concurrency race to a conflict (FR-BKG-08, FR-STF-03, FR-ACS-08).
/// </summary>
public class AppointmentStatusServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid PatientUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid StaffUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task CheckInAsync_WhenTheAppointmentIsTodayInTheStaffClinic_ChecksItIn()
    {
        var appointment = NewAppointment();
        var store = new FakeAppointmentStatusStore
        {
            CheckInContext = new CheckInContext(
                appointment,
                NewDoctor(clinicId: 1),
                NewStaff(clinicId: 1),
                NewPatient())
        };
        var service = CreateService(store);

        var result = await service.CheckInAsync(appointment.Id, StaffUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentStatus.CheckedIn, appointment.Status);
        Assert.Equal(Now.UtcDateTime, appointment.CheckedInAtUtc);
        Assert.Equal(1, store.SaveCount);

        var dto = Assert.IsType<StaffAppointmentDto>(result.Appointment);
        Assert.Equal(appointment.Id, dto.AppointmentId);
        Assert.Equal("Patient Integration Test", dto.PatientName);
        Assert.Equal("+20 100 000 0000", dto.PatientPhone);
        Assert.Equal(Now.UtcDateTime, dto.StartTimeUtc);
        Assert.Equal(nameof(AppointmentStatus.CheckedIn), dto.Status);
        Assert.Equal(Now.UtcDateTime, dto.CheckedInAtUtc);
    }

    [Fact]
    public async Task CheckInAsync_WhenTheAppointmentDoesNotExist_ReturnsAppointmentNotFound()
    {
        var store = new FakeAppointmentStatusStore { CheckInContext = null };
        var service = CreateService(store);

        var result = await service.CheckInAsync(42, StaffUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(CheckInFailure.AppointmentNotFound, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task CheckInAsync_WhenTheUserHasNoStaffProfile_ReturnsStaffNotFound()
    {
        var appointment = NewAppointment();
        var store = new FakeAppointmentStatusStore
        {
            CheckInContext = new CheckInContext(
                appointment,
                NewDoctor(clinicId: 1),
                Staff: null,
                NewPatient())
        };
        var service = CreateService(store);

        var result = await service.CheckInAsync(appointment.Id, StaffUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(CheckInFailure.StaffNotFound, result.Failure);
        Assert.Equal(AppointmentStatus.Booked, appointment.Status);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task CheckInAsync_WhenTheDoctorProfileIsMissing_ReturnsAppointmentNotFound()
    {
        var appointment = NewAppointment();
        var store = new FakeAppointmentStatusStore
        {
            CheckInContext = new CheckInContext(
                appointment,
                Doctor: null,
                NewStaff(clinicId: 1),
                NewPatient())
        };
        var service = CreateService(store);

        var result = await service.CheckInAsync(appointment.Id, StaffUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(CheckInFailure.AppointmentNotFound, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task CheckInAsync_WhenThePatientAccountIsMissing_ReturnsPatientNotFound()
    {
        var appointment = NewAppointment();
        var store = new FakeAppointmentStatusStore
        {
            CheckInContext = new CheckInContext(
                appointment,
                NewDoctor(clinicId: 1),
                NewStaff(clinicId: 1),
                Patient: null)
        };
        var service = CreateService(store);

        var result = await service.CheckInAsync(appointment.Id, StaffUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(CheckInFailure.PatientNotFound, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task CheckInAsync_WhenTheAppointmentIsInAnotherClinic_ReturnsClinicMismatch()
    {
        var appointment = NewAppointment();
        var store = new FakeAppointmentStatusStore
        {
            CheckInContext = new CheckInContext(
                appointment,
                NewDoctor(clinicId: 1),
                NewStaff(clinicId: 2),
                NewPatient())
        };
        var service = CreateService(store);

        var result = await service.CheckInAsync(appointment.Id, StaffUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(CheckInFailure.ClinicMismatch, result.Failure);
        Assert.Equal(AppointmentStatus.Booked, appointment.Status);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task CheckInAsync_WhenTheSlotIsNotOnTheClinicLocalDate_ReturnsAppointmentNotToday()
    {
        var appointment = NewAppointment(slotStartUtc: Now.UtcDateTime.AddDays(1));
        var store = new FakeAppointmentStatusStore
        {
            CheckInContext = new CheckInContext(
                appointment,
                NewDoctor(clinicId: 1),
                NewStaff(clinicId: 1),
                NewPatient())
        };
        var service = CreateService(store);

        var result = await service.CheckInAsync(appointment.Id, StaffUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(CheckInFailure.AppointmentNotToday, result.Failure);
        Assert.Equal(AppointmentStatus.Booked, appointment.Status);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task CheckInAsync_UsesTheCairoCalendarForTheTodayCheck()
    {
        // 22:30 UTC is already the next day in Cairo, so a slot at 21:00 UTC belongs to the previous local day.
        var appointment = NewAppointment(slotStartUtc: new DateTime(2026, 11, 8, 21, 0, 0, DateTimeKind.Utc));
        var store = new FakeAppointmentStatusStore
        {
            CheckInContext = new CheckInContext(
                appointment,
                NewDoctor(clinicId: 1),
                NewStaff(clinicId: 1),
                NewPatient())
        };
        var service = new AppointmentStatusService(
            store,
            new FakeClinicTimeZone { Offset = TimeSpan.FromHours(2) },
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 8, 22, 30, 0, TimeSpan.Zero)));

        var result = await service.CheckInAsync(appointment.Id, StaffUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(CheckInFailure.AppointmentNotToday, result.Failure);
    }

    [Theory]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.InVisit)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task CheckInAsync_WhenTheAppointmentIsNotBooked_ReturnsAppointmentNotBooked(
        AppointmentStatus status)
    {
        var appointment = NewAppointment(status: status);
        var store = new FakeAppointmentStatusStore
        {
            CheckInContext = new CheckInContext(
                appointment,
                NewDoctor(clinicId: 1),
                NewStaff(clinicId: 1),
                NewPatient())
        };
        var service = CreateService(store);

        var result = await service.CheckInAsync(appointment.Id, StaffUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(CheckInFailure.AppointmentNotBooked, result.Failure);
        Assert.Equal(status, appointment.Status);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task CheckInAsync_WhenAConcurrentRequestWon_ReturnsAppointmentConflict()
    {
        var appointment = NewAppointment();
        var store = new FakeAppointmentStatusStore
        {
            CheckInContext = new CheckInContext(
                appointment,
                NewDoctor(clinicId: 1),
                NewStaff(clinicId: 1),
                NewPatient()),
            ConflictOnSave = true
        };
        var service = CreateService(store);

        var result = await service.CheckInAsync(appointment.Id, StaffUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(CheckInFailure.AppointmentConflict, result.Failure);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task CompleteAsync_WhenTheDoctorOwnsAnInVisitAppointment_CompletesIt()
    {
        var appointment = NewAppointment(status: AppointmentStatus.InVisit);
        var store = new FakeAppointmentStatusStore
        {
            CompleteContext = new CompleteContext(appointment, NewDoctor(clinicId: 1))
        };
        var service = CreateService(store);

        var result = await service.CompleteAsync(appointment.Id, DoctorUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Equal(Now.UtcDateTime, appointment.CompletedAtUtc);
        Assert.Equal(1, store.SaveCount);

        var dto = Assert.IsType<DoctorAppointmentStatusDto>(result.Appointment);
        Assert.Equal(appointment.Id, dto.AppointmentId);
        Assert.Equal(nameof(AppointmentStatus.Completed), dto.Status);
        Assert.Equal(Now.UtcDateTime, dto.CompletedAtUtc);
    }

    [Fact]
    public async Task CompleteAsync_WhenTheAppointmentDoesNotExist_ReturnsAppointmentNotFound()
    {
        var store = new FakeAppointmentStatusStore { CompleteContext = null };
        var service = CreateService(store);

        var result = await service.CompleteAsync(42, DoctorUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(CompleteFailure.AppointmentNotFound, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task CompleteAsync_WhenTheUserHasNoDoctorProfile_ReturnsDoctorNotFound()
    {
        var appointment = NewAppointment(status: AppointmentStatus.InVisit);
        var store = new FakeAppointmentStatusStore
        {
            CompleteContext = new CompleteContext(appointment, Doctor: null)
        };
        var service = CreateService(store);

        var result = await service.CompleteAsync(appointment.Id, DoctorUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(CompleteFailure.DoctorNotFound, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task CompleteAsync_WhenTheDoctorDoesNotOwnTheAppointment_ReturnsDoctorMismatch()
    {
        var appointment = NewAppointment(status: AppointmentStatus.InVisit);
        var otherDoctor = NewDoctor(clinicId: 1, id: 8);
        var store = new FakeAppointmentStatusStore
        {
            CompleteContext = new CompleteContext(appointment, otherDoctor)
        };
        var service = CreateService(store);

        var result = await service.CompleteAsync(appointment.Id, DoctorUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(CompleteFailure.DoctorMismatch, result.Failure);
        Assert.Equal(AppointmentStatus.InVisit, appointment.Status);
        Assert.Equal(0, store.SaveCount);
    }

    [Theory]
    [InlineData(AppointmentStatus.Booked)]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task CompleteAsync_WhenTheAppointmentIsNotInVisit_ReturnsAppointmentNotInVisit(
        AppointmentStatus status)
    {
        var appointment = NewAppointment(status: status);
        var store = new FakeAppointmentStatusStore
        {
            CompleteContext = new CompleteContext(appointment, NewDoctor(clinicId: 1))
        };
        var service = CreateService(store);

        var result = await service.CompleteAsync(appointment.Id, DoctorUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(CompleteFailure.AppointmentNotInVisit, result.Failure);
        Assert.Equal(status, appointment.Status);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task CompleteAsync_WhenAConcurrentRequestWon_ReturnsAppointmentConflict()
    {
        var appointment = NewAppointment(status: AppointmentStatus.InVisit);
        var store = new FakeAppointmentStatusStore
        {
            CompleteContext = new CompleteContext(appointment, NewDoctor(clinicId: 1)),
            ConflictOnSave = true
        };
        var service = CreateService(store);

        var result = await service.CompleteAsync(appointment.Id, DoctorUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(CompleteFailure.AppointmentConflict, result.Failure);
        Assert.Equal(1, store.SaveCount);
    }

    private static AppointmentStatusService CreateService(FakeAppointmentStatusStore store) =>
        new(store, new FakeClinicTimeZone(), new FixedTimeProvider(Now));

    private static Appointment NewAppointment(
        AppointmentStatus status = AppointmentStatus.Booked,
        DateTime? slotStartUtc = null)
    {
        var start = slotStartUtc ?? Now.UtcDateTime;
        var slot = new DoctorSlot
        {
            Id = 3,
            DoctorId = 7,
            StartTimeUtc = start,
            EndTimeUtc = start.AddMinutes(30),
            IsAvailable = false
        };

        return new Appointment
        {
            Id = 42,
            PatientId = PatientUserId,
            DoctorId = 7,
            DoctorSlotId = slot.Id,
            DoctorSlot = slot,
            Status = status,
            CreatedAtUtc = Now.UtcDateTime.AddDays(-1)
        };
    }

    private static Doctor NewDoctor(int clinicId, int id = 7) =>
        new()
        {
            Id = id,
            UserId = DoctorUserId,
            Specialty = "General",
            ClinicId = clinicId
        };

    private static ClinicStaff NewStaff(int clinicId) =>
        new()
        {
            Id = 5,
            UserId = StaffUserId,
            ClinicId = clinicId
        };

    private static ApplicationUser NewPatient() =>
        new()
        {
            Id = PatientUserId,
            FullName = "Patient Integration Test",
            PhoneNumber = "+20 100 000 0000",
            Role = "Patient",
            IsActive = true
        };
}

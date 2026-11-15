using MediPassPlus.Application.Booking;
using MediPassPlus.Application.Common;
using MediPassPlus.Application.Tests.VisitAccess;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;

namespace MediPassPlus.Application.Tests.Booking;

public class BookingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task BookAsync_WhenTheSlotIsFree_BooksItAndMarksItUnavailable()
    {
        var slot = CreateSlot();
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            BookingSlotContext = new BookingSlotContext(
                slot,
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId),
                HasActiveAppointment: false)
        };
        var service = CreateService(store);

        var result = await service.BookAsync(doctor.Id, slot.Id, PatientId);

        Assert.True(result.IsSuccess);
        var appointment = Assert.IsType<AppointmentDto>(result.Appointment);
        Assert.Equal(PatientId, appointment.PatientId);
        Assert.Equal(doctor.Id, appointment.DoctorId);
        Assert.Equal(slot.Id, appointment.DoctorSlotId);
        Assert.Equal(nameof(AppointmentStatus.Booked), appointment.Status);
        Assert.Equal(slot.StartTimeUtc, appointment.StartTimeUtc);
        Assert.Equal(slot.EndTimeUtc, appointment.EndTimeUtc);

        Assert.False(slot.IsAvailable);
        var added = Assert.IsType<Appointment>(store.AddedAppointment);
        Assert.Equal(PatientId, added.PatientId);
        Assert.Equal(doctor.Id, added.DoctorId);
        Assert.Equal(slot.Id, added.DoctorSlotId);
        Assert.Equal(AppointmentStatus.Booked, added.Status);
        Assert.Equal(Now.UtcDateTime, added.CreatedAtUtc);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task BookAsync_WhenTheSlotDoesNotExist_ReturnsSlotNotFoundWithoutWriting()
    {
        var store = new FakeBookingStore { BookingSlotContext = null };
        var service = CreateService(store);

        var result = await service.BookAsync(7, 1, PatientId);

        Assert.False(result.IsSuccess);
        Assert.Equal(BookingFailure.SlotNotFound, result.Failure);
        Assert.Null(store.AddedAppointment);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task BookAsync_WhenTheDoctorDoesNotExist_ReturnsDoctorNotFound()
    {
        var store = new FakeBookingStore
        {
            BookingSlotContext = new BookingSlotContext(
                CreateSlot(),
                Doctor: null,
                DoctorUser: null,
                NewUser(PatientId),
                HasActiveAppointment: false)
        };

        var result = await CreateService(store).BookAsync(7, 1, PatientId);

        Assert.Equal(BookingFailure.DoctorNotFound, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task BookAsync_WhenTheDoctorAccountIsMissing_ReturnsDoctorNotFound()
    {
        var slot = CreateSlot();
        var store = new FakeBookingStore
        {
            BookingSlotContext = new BookingSlotContext(
                slot,
                CreateDoctor(),
                DoctorUser: null,
                NewUser(PatientId),
                HasActiveAppointment: false)
        };

        var result = await CreateService(store).BookAsync(7, slot.Id, PatientId);

        Assert.Equal(BookingFailure.DoctorNotFound, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task BookAsync_WhenThePatientDoesNotExist_ReturnsPatientNotFound()
    {
        var slot = CreateSlot();
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            BookingSlotContext = new BookingSlotContext(
                slot,
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                PatientUser: null,
                HasActiveAppointment: false)
        };

        var result = await CreateService(store).BookAsync(doctor.Id, slot.Id, PatientId);

        Assert.Equal(BookingFailure.PatientNotFound, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task BookAsync_WhenTheSlotBelongsToAnotherDoctor_ReturnsSlotDoctorMismatch()
    {
        var slot = CreateSlot(doctorId: 8);
        var doctor = CreateDoctor(id: 7);
        var store = new FakeBookingStore
        {
            BookingSlotContext = new BookingSlotContext(
                slot,
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId),
                HasActiveAppointment: false)
        };

        var result = await CreateService(store).BookAsync(doctor.Id, slot.Id, PatientId);

        Assert.Equal(BookingFailure.SlotDoctorMismatch, result.Failure);
        Assert.True(slot.IsAvailable);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task BookAsync_WhenTheDoctorIsInactive_ReturnsDoctorInactive()
    {
        var slot = CreateSlot();
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            BookingSlotContext = new BookingSlotContext(
                slot,
                doctor,
                NewUser(doctor.UserId, isActive: false, role: "Doctor"),
                NewUser(PatientId),
                HasActiveAppointment: false)
        };

        var result = await CreateService(store).BookAsync(doctor.Id, slot.Id, PatientId);

        Assert.Equal(BookingFailure.DoctorInactive, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task BookAsync_WhenThePatientIsInactive_ReturnsPatientInactive()
    {
        var slot = CreateSlot();
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            BookingSlotContext = new BookingSlotContext(
                slot,
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId, isActive: false),
                HasActiveAppointment: false)
        };

        var result = await CreateService(store).BookAsync(doctor.Id, slot.Id, PatientId);

        Assert.Equal(BookingFailure.PatientInactive, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task BookAsync_WhenTheSlotIsInThePast_ReturnsSlotInPast()
    {
        var slot = CreateSlot(startUtc: Now.UtcDateTime.AddMinutes(-1));
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            BookingSlotContext = new BookingSlotContext(
                slot,
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId),
                HasActiveAppointment: false)
        };

        var result = await CreateService(store).BookAsync(doctor.Id, slot.Id, PatientId);

        Assert.Equal(BookingFailure.SlotInPast, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task BookAsync_WhenTheSlotStartsExactlyNow_ReturnsSlotInPast()
    {
        var slot = CreateSlot(startUtc: Now.UtcDateTime);
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            BookingSlotContext = new BookingSlotContext(
                slot,
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId),
                HasActiveAppointment: false)
        };

        var result = await CreateService(store).BookAsync(doctor.Id, slot.Id, PatientId);

        Assert.Equal(BookingFailure.SlotInPast, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task BookAsync_WhenTheSlotIsMarkedUnavailable_ReturnsSlotAlreadyBooked()
    {
        var slot = CreateSlot(isAvailable: false);
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            BookingSlotContext = new BookingSlotContext(
                slot,
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId),
                HasActiveAppointment: false)
        };

        var result = await CreateService(store).BookAsync(doctor.Id, slot.Id, PatientId);

        Assert.Equal(BookingFailure.SlotAlreadyBooked, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task BookAsync_WhenAnActiveAppointmentAlreadyUsesTheSlot_ReturnsSlotAlreadyBooked()
    {
        var slot = CreateSlot();
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            BookingSlotContext = new BookingSlotContext(
                slot,
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId),
                HasActiveAppointment: true)
        };

        var result = await CreateService(store).BookAsync(doctor.Id, slot.Id, PatientId);

        Assert.Equal(BookingFailure.SlotAlreadyBooked, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task BookAsync_WhenAConcurrentRequestWinsTheRace_ReturnsSlotAlreadyBooked()
    {
        var slot = CreateSlot();
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            BookingSlotContext = new BookingSlotContext(
                slot,
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId),
                HasActiveAppointment: false),
            ConflictOnSave = true
        };

        var result = await CreateService(store).BookAsync(doctor.Id, slot.Id, PatientId);

        Assert.Equal(BookingFailure.SlotAlreadyBooked, result.Failure);
        Assert.Equal(1, store.SaveCount);
        Assert.NotNull(store.AddedAppointment);
    }

    [Fact]
    public async Task CancelAsync_WhenTheAppointmentIsBookedAndFuture_CancelsItAndFreesTheSlot()
    {
        var slot = CreateSlot();
        var appointment = CreateAppointment(slot);
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                appointment,
                doctor,
                NewUser(DoctorUserId, role: "Doctor"),
                NewUser(PatientId))
        };

        var result = await CreateService(store).CancelAsync(appointment.Id, PatientId);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.True(slot.IsAvailable);
        Assert.Equal(doctor.Id, appointment.DoctorId);
        Assert.Equal(PatientId, appointment.PatientId);
        Assert.Equal(slot.Id, appointment.DoctorSlotId);
        Assert.Equal(nameof(AppointmentStatus.Cancelled), result.Appointment!.Status);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task CancelAsync_WhenTheAppointmentIsNotInScope_ReturnsAppointmentNotFound()
    {
        var store = new FakeBookingStore { AppointmentContext = null };

        var result = await CreateService(store).CancelAsync(42, PatientId);

        Assert.Equal(BookingFailure.AppointmentNotFound, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task CancelAsync_WhenThePatientAccountIsMissing_ReturnsPatientNotFound()
    {
        var slot = CreateSlot();
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(slot),
                CreateDoctor(),
                NewUser(DoctorUserId, role: "Doctor"),
                PatientUser: null)
        };

        var result = await CreateService(store).CancelAsync(42, PatientId);

        Assert.Equal(BookingFailure.PatientNotFound, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task CancelAsync_WhenThePatientIsInactive_ReturnsPatientInactive()
    {
        var slot = CreateSlot();
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(slot),
                CreateDoctor(),
                NewUser(DoctorUserId, role: "Doctor"),
                NewUser(PatientId, isActive: false))
        };

        var result = await CreateService(store).CancelAsync(42, PatientId);

        Assert.Equal(BookingFailure.PatientInactive, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Theory]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.InVisit)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task CancelAsync_WhenTheAppointmentIsNotBooked_ReturnsAppointmentNotCancellable(
        AppointmentStatus status)
    {
        var slot = CreateSlot();
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(slot, status),
                CreateDoctor(),
                NewUser(DoctorUserId, role: "Doctor"),
                NewUser(PatientId))
        };

        var result = await CreateService(store).CancelAsync(42, PatientId);

        Assert.Equal(BookingFailure.AppointmentNotCancellable, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task CancelAsync_WhenTheSlotHasStarted_ReturnsAppointmentAlreadyStarted()
    {
        var slot = CreateSlot(startUtc: Now.UtcDateTime.AddMinutes(-1));
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(slot),
                CreateDoctor(),
                NewUser(DoctorUserId, role: "Doctor"),
                NewUser(PatientId))
        };

        var result = await CreateService(store).CancelAsync(42, PatientId);

        Assert.Equal(BookingFailure.AppointmentAlreadyStarted, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task CancelAsync_DoesNotRequireTheDoctorToBeActive()
    {
        var slot = CreateSlot();
        var appointment = CreateAppointment(slot);
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                appointment,
                CreateDoctor(),
                NewUser(DoctorUserId, isActive: false, role: "Doctor"),
                NewUser(PatientId))
        };

        var result = await CreateService(store).CancelAsync(appointment.Id, PatientId);

        Assert.True(result.IsSuccess);
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.True(slot.IsAvailable);
    }

    [Fact]
    public async Task CancelAsync_DoesNotRequireTheDoctorProfileToExist()
    {
        var slot = CreateSlot();
        var appointment = CreateAppointment(slot);
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                appointment,
                Doctor: null,
                DoctorUser: null,
                NewUser(PatientId))
        };

        var result = await CreateService(store).CancelAsync(appointment.Id, PatientId);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task CancelAsync_WhenAConcurrentRequestWinsTheRace_ReturnsAppointmentConflict()
    {
        var slot = CreateSlot();
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(slot),
                CreateDoctor(),
                NewUser(DoctorUserId, role: "Doctor"),
                NewUser(PatientId)),
            ConflictOnSave = true
        };

        var result = await CreateService(store).CancelAsync(42, PatientId);

        Assert.Equal(BookingFailure.AppointmentConflict, result.Failure);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task RescheduleAsync_WhenTheTargetSlotIsFree_MovesTheAppointmentAndSwapsAvailability()
    {
        var currentSlot = CreateSlot(id: 1);
        var targetSlot = CreateSlot(id: 2, startUtc: Now.UtcDateTime.AddHours(2));
        var appointment = CreateAppointment(currentSlot);
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                appointment,
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId)),
            TargetSlotContext = new TargetSlotContext(targetSlot, HasActiveAppointment: false)
        };

        var result = await CreateService(store).RescheduleAsync(appointment.Id, targetSlot.Id, PatientId);

        Assert.True(result.IsSuccess);
        Assert.Equal(targetSlot.Id, appointment.DoctorSlotId);
        Assert.Equal(doctor.Id, appointment.DoctorId);
        Assert.Equal(PatientId, appointment.PatientId);
        Assert.True(currentSlot.IsAvailable);
        Assert.False(targetSlot.IsAvailable);
        Assert.Equal(targetSlot.Id, result.Appointment!.DoctorSlotId);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task RescheduleAsync_WhenTheAppointmentIsNotInScope_ReturnsAppointmentNotFound()
    {
        var store = new FakeBookingStore { AppointmentContext = null };

        var result = await CreateService(store).RescheduleAsync(42, 2, PatientId);

        Assert.Equal(BookingFailure.AppointmentNotFound, result.Failure);
        Assert.False(store.TargetSlotLoaded);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RescheduleAsync_WhenTheDoctorProfileIsMissing_ReturnsDoctorNotFound()
    {
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(CreateSlot()),
                Doctor: null,
                DoctorUser: null,
                NewUser(PatientId))
        };

        var result = await CreateService(store).RescheduleAsync(42, 2, PatientId);

        Assert.Equal(BookingFailure.DoctorNotFound, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RescheduleAsync_WhenTheDoctorIsInactive_ReturnsDoctorInactive()
    {
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(CreateSlot()),
                doctor,
                NewUser(doctor.UserId, isActive: false, role: "Doctor"),
                NewUser(PatientId))
        };

        var result = await CreateService(store).RescheduleAsync(42, 2, PatientId);

        Assert.Equal(BookingFailure.DoctorInactive, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RescheduleAsync_WhenThePatientAccountIsMissing_ReturnsPatientNotFound()
    {
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(CreateSlot()),
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                PatientUser: null)
        };

        var result = await CreateService(store).RescheduleAsync(42, 2, PatientId);

        Assert.Equal(BookingFailure.PatientNotFound, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RescheduleAsync_WhenThePatientIsInactive_ReturnsPatientInactive()
    {
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(CreateSlot()),
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId, isActive: false))
        };

        var result = await CreateService(store).RescheduleAsync(42, 2, PatientId);

        Assert.Equal(BookingFailure.PatientInactive, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Theory]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.InVisit)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task RescheduleAsync_WhenTheAppointmentIsNotBooked_ReturnsAppointmentNotReschedulable(
        AppointmentStatus status)
    {
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(CreateSlot(), status),
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId))
        };

        var result = await CreateService(store).RescheduleAsync(42, 2, PatientId);

        Assert.Equal(BookingFailure.AppointmentNotReschedulable, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RescheduleAsync_WhenTheCurrentSlotHasStarted_ReturnsAppointmentAlreadyStarted()
    {
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(CreateSlot(startUtc: Now.UtcDateTime.AddMinutes(-1))),
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId))
        };

        var result = await CreateService(store).RescheduleAsync(42, 2, PatientId);

        Assert.Equal(BookingFailure.AppointmentAlreadyStarted, result.Failure);
        Assert.False(store.TargetSlotLoaded);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RescheduleAsync_ToTheCurrentSlot_ReturnsAppointmentAlreadyOnSlot()
    {
        var slot = CreateSlot();
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(slot),
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId))
        };

        var result = await CreateService(store).RescheduleAsync(42, slot.Id, PatientId);

        Assert.Equal(BookingFailure.AppointmentAlreadyOnSlot, result.Failure);
        Assert.False(store.TargetSlotLoaded);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RescheduleAsync_WhenTheTargetSlotDoesNotExist_ReturnsSlotNotFound()
    {
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(CreateSlot()),
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId)),
            TargetSlotContext = null
        };

        var result = await CreateService(store).RescheduleAsync(42, 2, PatientId);

        Assert.Equal(BookingFailure.SlotNotFound, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RescheduleAsync_WhenTheTargetBelongsToAnotherDoctor_ReturnsSlotDoctorMismatch()
    {
        var doctor = CreateDoctor(id: 7);
        var target = CreateSlot(id: 2, doctorId: 8);
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(CreateSlot(doctorId: 7)),
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId)),
            TargetSlotContext = new TargetSlotContext(target, HasActiveAppointment: false)
        };

        var result = await CreateService(store).RescheduleAsync(42, target.Id, PatientId);

        Assert.Equal(BookingFailure.SlotDoctorMismatch, result.Failure);
        Assert.True(target.IsAvailable);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RescheduleAsync_WhenTheTargetIsInThePast_ReturnsSlotInPast()
    {
        var doctor = CreateDoctor();
        var target = CreateSlot(id: 2, startUtc: Now.UtcDateTime.AddMinutes(-1));
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(CreateSlot()),
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId)),
            TargetSlotContext = new TargetSlotContext(target, HasActiveAppointment: false)
        };

        var result = await CreateService(store).RescheduleAsync(42, target.Id, PatientId);

        Assert.Equal(BookingFailure.SlotInPast, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RescheduleAsync_WhenTheTargetIsUnavailable_ReturnsSlotAlreadyBooked()
    {
        var doctor = CreateDoctor();
        var target = CreateSlot(id: 2, isAvailable: false);
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(CreateSlot()),
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId)),
            TargetSlotContext = new TargetSlotContext(target, HasActiveAppointment: false)
        };

        var result = await CreateService(store).RescheduleAsync(42, target.Id, PatientId);

        Assert.Equal(BookingFailure.SlotAlreadyBooked, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RescheduleAsync_WhenAnActiveAppointmentUsesTheTarget_ReturnsSlotAlreadyBooked()
    {
        var doctor = CreateDoctor();
        var target = CreateSlot(id: 2);
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(CreateSlot()),
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId)),
            TargetSlotContext = new TargetSlotContext(target, HasActiveAppointment: true)
        };

        var result = await CreateService(store).RescheduleAsync(42, target.Id, PatientId);

        Assert.Equal(BookingFailure.SlotAlreadyBooked, result.Failure);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RescheduleAsync_WhenAConcurrentRequestWinsTheRace_ReturnsAppointmentConflict()
    {
        var currentSlot = CreateSlot();
        var target = CreateSlot(id: 2);
        var doctor = CreateDoctor();
        var store = new FakeBookingStore
        {
            AppointmentContext = new BookingAppointmentContext(
                CreateAppointment(currentSlot),
                doctor,
                NewUser(doctor.UserId, role: "Doctor"),
                NewUser(PatientId)),
            TargetSlotContext = new TargetSlotContext(target, HasActiveAppointment: false),
            ConflictOnSave = true
        };

        var result = await CreateService(store).RescheduleAsync(42, target.Id, PatientId);

        Assert.Equal(BookingFailure.AppointmentConflict, result.Failure);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task GetMyAppointmentsAsync_ReturnsThePatientsPageAndMapsTheRows()
    {
        var row = CreateRow(status: AppointmentStatus.CheckedIn);
        var store = new FakeBookingStore
        {
            AppointmentsPage = new PatientAppointmentsPage([row], TotalCount: 7)
        };
        var service = CreateService(store);
        var page = new PageRequest { Page = 2, PageSize = 20 };

        var result = await service.GetMyAppointmentsAsync(PatientId, page);

        Assert.Equal(PatientId, store.ListedPatientUserId);
        Assert.Equal(20, store.ListedSkip);
        Assert.Equal(20, store.ListedTake);
        Assert.Equal(7, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(20, result.PageSize);

        var appointment = Assert.Single(result.Items);
        Assert.Equal(row.Id, appointment.Id);
        Assert.Equal(row.DoctorId, appointment.DoctorId);
        Assert.Equal(row.DoctorName, appointment.DoctorName);
        Assert.Equal(row.ClinicId, appointment.ClinicId);
        Assert.Equal(row.ClinicName, appointment.ClinicName);
        Assert.Equal(row.DoctorSlotId, appointment.DoctorSlotId);
        Assert.Equal(row.StartTimeUtc, appointment.StartTimeUtc);
        Assert.Equal(row.EndTimeUtc, appointment.EndTimeUtc);
        Assert.Equal(nameof(AppointmentStatus.CheckedIn), appointment.Status);
        Assert.Equal(row.CreatedAtUtc, appointment.CreatedAtUtc);
    }

    [Fact]
    public async Task GetMyAppointmentsAsync_UsesTheDefaultPageWhenNoneIsRequested()
    {
        var store = new FakeBookingStore();
        var service = CreateService(store);

        var result = await service.GetMyAppointmentsAsync(PatientId, new PageRequest());

        Assert.Equal(0, store.ListedSkip);
        Assert.Equal(PageRequest.DefaultPageSize, store.ListedTake);
        Assert.Equal(PageRequest.DefaultPage, result.Page);
        Assert.Equal(PageRequest.DefaultPageSize, result.PageSize);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    private static BookingService CreateService(FakeBookingStore store) =>
        new(store, new FixedTimeProvider(Now));

    private static DoctorSlot CreateSlot(
        int id = 1,
        int doctorId = 7,
        DateTime? startUtc = null,
        bool isAvailable = true)
    {
        var start = startUtc ?? Now.UtcDateTime.AddHours(1);

        return new DoctorSlot
        {
            Id = id,
            DoctorId = doctorId,
            StartTimeUtc = start,
            EndTimeUtc = start.AddMinutes(30),
            IsAvailable = isAvailable
        };
    }

    private static Appointment CreateAppointment(
        DoctorSlot slot,
        AppointmentStatus status = AppointmentStatus.Booked) =>
        new()
        {
            Id = 42,
            PatientId = PatientId,
            DoctorId = slot.DoctorId,
            DoctorSlotId = slot.Id,
            DoctorSlot = slot,
            Status = status,
            CreatedAtUtc = Now.UtcDateTime.AddDays(-1)
        };

    private static PatientAppointmentRow CreateRow(
        int id = 42,
        AppointmentStatus status = AppointmentStatus.Booked) =>
        new(
            id,
            DoctorId: 7,
            DoctorName: "Doctor User",
            ClinicId: 1,
            ClinicName: "Booking Clinic",
            DoctorSlotId: 3,
            StartTimeUtc: Now.UtcDateTime.AddHours(1),
            EndTimeUtc: Now.UtcDateTime.AddHours(1).AddMinutes(30),
            Status: status,
            CreatedAtUtc: Now.UtcDateTime.AddDays(-1));

    private static Doctor CreateDoctor(int id = 7) =>
        new()
        {
            Id = id,
            UserId = DoctorUserId,
            Specialty = "General",
            ClinicId = 1
        };

    private static ApplicationUser NewUser(
        Guid id,
        bool isActive = true,
        string role = "Patient") =>
        new()
        {
            Id = id,
            UserName = $"{role.ToLowerInvariant()}@example.test",
            Email = $"{role.ToLowerInvariant()}@example.test",
            FullName = $"{role} User",
            Role = role,
            IsActive = isActive
        };
}

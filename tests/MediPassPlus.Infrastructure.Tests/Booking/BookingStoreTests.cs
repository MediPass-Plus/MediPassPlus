using MediPassPlus.Application.Booking;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Tests.Booking;

public class BookingStoreTests
{
    private static readonly DateTime Start = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task LoadBookingSlotAsync_ReturnsTheSlotDoctorAndBothUsers()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var patientId = Guid.NewGuid();
        var doctorUserId = Guid.NewGuid();
        SeedGraph(db, doctorUserId, patientId);
        await db.SaveChangesAsync();

        var store = new BookingStore(db);

        var context = await store.LoadBookingSlotAsync(2, 7, patientId);

        Assert.NotNull(context);
        Assert.Equal(2, context.Slot.Id);
        Assert.NotNull(context.Doctor);
        Assert.Equal(7, context.Doctor.Id);
        Assert.NotNull(context.DoctorUser);
        Assert.Equal(doctorUserId, context.DoctorUser.Id);
        Assert.NotNull(context.PatientUser);
        Assert.Equal(patientId, context.PatientUser.Id);
        Assert.False(context.HasActiveAppointment);
    }

    [Fact]
    public async Task LoadBookingSlotAsync_ForAnUnknownSlot_ReturnsNull()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var store = new BookingStore(db);

        Assert.Null(await store.LoadBookingSlotAsync(999, 7, Guid.NewGuid()));
    }

    [Fact]
    public async Task LoadBookingSlotAsync_ReportsAnActiveAppointmentOnTheSlot()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var patientId = Guid.NewGuid();
        SeedGraph(db, Guid.NewGuid(), patientId);
        await db.SaveChangesAsync();

        var store = new BookingStore(db);

        var context = await store.LoadBookingSlotAsync(1, 7, patientId);

        Assert.NotNull(context);
        Assert.True(context.HasActiveAppointment);
    }

    [Fact]
    public async Task LoadAppointmentAsync_ForAnotherPatientsAppointment_ReturnsNull()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var patientId = Guid.NewGuid();
        SeedGraph(db, Guid.NewGuid(), patientId);
        await db.SaveChangesAsync();

        var store = new BookingStore(db);

        Assert.Null(await store.LoadAppointmentAsync(42, Guid.NewGuid()));
    }

    [Fact]
    public async Task LoadAppointmentAsync_ReturnsTheScopedAppointmentWithItsSlot()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var patientId = Guid.NewGuid();
        var doctorUserId = Guid.NewGuid();
        SeedGraph(db, doctorUserId, patientId);
        await db.SaveChangesAsync();

        var store = new BookingStore(db);

        var context = await store.LoadAppointmentAsync(42, patientId);

        Assert.NotNull(context);
        Assert.Equal(42, context.Appointment.Id);
        Assert.NotNull(context.Appointment.DoctorSlot);
        Assert.NotNull(context.Doctor);
        Assert.Equal(doctorUserId, context.DoctorUser?.Id);
        Assert.Equal(patientId, context.PatientUser?.Id);
    }

    [Fact]
    public async Task LoadTargetSlotAsync_ReportsWhetherAnActiveAppointmentUsesTheSlot()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        SeedGraph(db, Guid.NewGuid(), Guid.NewGuid());
        await db.SaveChangesAsync();

        var store = new BookingStore(db);

        var occupied = await store.LoadTargetSlotAsync(1);
        var free = await store.LoadTargetSlotAsync(2);

        Assert.NotNull(occupied);
        Assert.True(occupied.HasActiveAppointment);
        Assert.NotNull(free);
        Assert.False(free.HasActiveAppointment);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenASecondActiveAppointmentUsesTheSlot_ThrowsBookingConflict()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var patientId = Guid.NewGuid();
        SeedGraph(db, Guid.NewGuid(), patientId);
        await db.SaveChangesAsync();

        db.Appointments.Add(new Appointment
        {
            PatientId = patientId,
            DoctorId = 7,
            DoctorSlotId = 1,
            Status = AppointmentStatus.Booked,
            CreatedAtUtc = Start
        });

        var store = new BookingStore(db);

        var exception = await Assert.ThrowsAsync<BookingConflictException>(
            () => store.SaveChangesAsync());

        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAConcurrencyTokenWasChanged_ThrowsBookingConflict()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        SeedGraph(db, Guid.NewGuid(), Guid.NewGuid());
        await db.SaveChangesAsync();

        var slot = await db.DoctorSlots.SingleAsync(slot => slot.Id == 2);
        slot.IsAvailable = false;

        // Pretend another request already changed the row after this context read it.
        db.Entry(slot).Property(s => s.IsAvailable).OriginalValue = false;

        var store = new BookingStore(db);

        var exception = await Assert.ThrowsAsync<BookingConflictException>(
            () => store.SaveChangesAsync());

        Assert.IsType<DbUpdateConcurrencyException>(exception.InnerException);
    }

    private static void SeedGraph(MediPassDbContext db, Guid doctorUserId, Guid patientId)
    {
        db.Users.AddRange(
            NewUser(doctorUserId, "doctor@example.test", "Doctor"),
            NewUser(patientId, "patient@example.test", "Patient"));
        db.Clinics.Add(new Clinic { Id = 1, Name = "Test clinic", Address = "Test address" });
        db.Doctors.Add(new Doctor
        {
            Id = 7,
            UserId = doctorUserId,
            Specialty = "General",
            ClinicId = 1
        });
        db.DoctorSlots.AddRange(
            new DoctorSlot
            {
                Id = 1,
                DoctorId = 7,
                StartTimeUtc = Start.AddHours(1),
                EndTimeUtc = Start.AddHours(1).AddMinutes(30),
                IsAvailable = false
            },
            new DoctorSlot
            {
                Id = 2,
                DoctorId = 7,
                StartTimeUtc = Start.AddHours(2),
                EndTimeUtc = Start.AddHours(2).AddMinutes(30),
                IsAvailable = true
            });
        db.Appointments.Add(new Appointment
        {
            Id = 42,
            PatientId = patientId,
            DoctorId = 7,
            DoctorSlotId = 1,
            Status = AppointmentStatus.Booked,
            CreatedAtUtc = Start.AddDays(-1)
        });
    }

    private static ApplicationUser NewUser(Guid id, string email, string role) =>
        new()
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FullName = role + " User",
            Role = role,
            IsActive = true
        };
}

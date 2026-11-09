using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Tests.Privacy;

public class AccessGrantStoreTests
{
    private static readonly DateTime Start = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FindGrantAsync_ReturnsTheGrantForTheExactScope()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var scope = await SeedGrantAsync(db);
        var store = new AccessGrantStore(db);

        var grant = await store.FindGrantAsync(
            scope.AppointmentId,
            scope.DoctorId,
            scope.PatientId,
            CancellationToken.None);

        Assert.NotNull(grant);
        Assert.Equal(scope.GrantId, grant.Id);
        Assert.Equal(scope.AppointmentId, grant.AppointmentId);
        Assert.Equal(scope.DoctorId, grant.DoctorId);
        Assert.Equal(scope.PatientId, grant.PatientId);
    }

    [Fact]
    public async Task FindGrantAsync_ForAnotherDoctor_ReturnsNull()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var scope = await SeedGrantAsync(db);
        var store = new AccessGrantStore(db);

        var grant = await store.FindGrantAsync(
            scope.AppointmentId,
            scope.DoctorId + 1,
            scope.PatientId,
            CancellationToken.None);

        Assert.Null(grant);
    }

    [Fact]
    public async Task FindGrantAsync_ForAnotherPatient_ReturnsNull()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var scope = await SeedGrantAsync(db);
        var store = new AccessGrantStore(db);

        var grant = await store.FindGrantAsync(
            scope.AppointmentId,
            scope.DoctorId,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Null(grant);
    }

    [Fact]
    public async Task FindGrantAsync_ForAnotherAppointment_ReturnsNull()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var scope = await SeedGrantAsync(db);
        var store = new AccessGrantStore(db);

        var grant = await store.FindGrantAsync(
            scope.AppointmentId + 1,
            scope.DoctorId,
            scope.PatientId,
            CancellationToken.None);

        Assert.Null(grant);
    }

    private static async Task<GrantScope> SeedGrantAsync(MediPassDbContext db)
    {
        var doctorUserId = Guid.NewGuid();
        var patientId = Guid.NewGuid();

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

        db.DoctorSlots.Add(new DoctorSlot
        {
            Id = 1,
            DoctorId = 7,
            StartTimeUtc = Start,
            EndTimeUtc = Start.AddMinutes(30)
        });

        var appointment = new Appointment
        {
            Id = 42,
            PatientId = patientId,
            DoctorId = 7,
            DoctorSlotId = 1,
            Status = AppointmentStatus.CheckedIn,
            CreatedAtUtc = Start.AddDays(-1)
        };

        var doctor = db.Doctors.Local.Single(candidate => candidate.Id == 7);
        var request = AccessRequest.Create(appointment, doctor, Start.AddMinutes(-5));
        request.Approve();

        var grant = AccessGrant.Create(request, appointment, Start);

        db.Appointments.Add(appointment);
        db.AccessRequests.Add(request);
        db.AccessGrants.Add(grant);

        await db.SaveChangesAsync();

        return new GrantScope(appointment.Id, doctor.Id, patientId, grant.Id);
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

    private sealed record GrantScope(int AppointmentId, int DoctorId, Guid PatientId, Guid GrantId);
}

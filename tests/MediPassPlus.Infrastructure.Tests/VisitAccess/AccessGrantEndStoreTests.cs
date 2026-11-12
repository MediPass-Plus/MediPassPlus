using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Tests.VisitAccess;

public class AccessGrantEndStoreTests
{
    private static readonly DateTime Start = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FindGrantAsync_ReturnsTheGrantWithoutTrackingIt()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var scope = await SeedGrantAsync(db);
        db.ChangeTracker.Clear();
        var store = new AccessGrantEndStore(db);

        var grant = await store.FindGrantAsync(scope.GrantId);

        Assert.NotNull(grant);
        Assert.Equal(scope.GrantId, grant.Id);
        Assert.Equal(scope.AppointmentId, grant.AppointmentId);
        Assert.Equal(scope.DoctorId, grant.DoctorId);
        Assert.Equal(scope.PatientId, grant.PatientId);
        Assert.Empty(db.ChangeTracker.Entries<AccessGrant>());
    }

    [Fact]
    public async Task FindGrantAsync_ForAnUnknownGrant_ReturnsNull()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        await SeedGrantAsync(db);
        var store = new AccessGrantEndStore(db);

        var grant = await store.FindGrantAsync(Guid.NewGuid());

        Assert.Null(grant);
    }

    [Fact]
    public async Task LoadAsync_WithTheExactScope_ReturnsTheTrackedGrant()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var scope = await SeedGrantAsync(db);
        db.ChangeTracker.Clear();
        var store = new AccessGrantEndStore(db);

        var grant = await store.LoadAsync(
            scope.GrantId,
            new VisitAccessScope(scope.AppointmentId, scope.DoctorId, scope.PatientId));

        Assert.NotNull(grant);
        Assert.Equal(scope.GrantId, grant.Id);
        Assert.Single(db.ChangeTracker.Entries<AccessGrant>());
    }

    [Fact]
    public async Task LoadAsync_ForAnotherPatient_ReturnsNull()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var scope = await SeedGrantAsync(db);
        var store = new AccessGrantEndStore(db);

        var grant = await store.LoadAsync(
            scope.GrantId,
            new VisitAccessScope(scope.AppointmentId, scope.DoctorId, Guid.NewGuid()));

        Assert.Null(grant);
    }

    [Fact]
    public async Task LoadAsync_ForAnotherAppointment_ReturnsNull()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var scope = await SeedGrantAsync(db);
        var store = new AccessGrantEndStore(db);

        var grant = await store.LoadAsync(
            scope.GrantId,
            new VisitAccessScope(scope.AppointmentId + 1, scope.DoctorId, scope.PatientId));

        Assert.Null(grant);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsThePatientEnd()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var scope = await SeedGrantAsync(db);
        db.ChangeTracker.Clear();
        var store = new AccessGrantEndStore(db);
        var tracked = await store.LoadAsync(
            scope.GrantId,
            new VisitAccessScope(scope.AppointmentId, scope.DoctorId, scope.PatientId));

        Assert.NotNull(tracked);
        tracked.EndByPatient(Start.AddMinutes(20));
        await store.SaveChangesAsync();

        await using var reloadedDb = new MediPassDbContext(options);
        var reloaded = await reloadedDb.AccessGrants.SingleAsync(grant => grant.Id == scope.GrantId);

        Assert.Equal(Start.AddMinutes(20), reloaded.EndedAtUtc);
        Assert.Equal(GrantEndReason.PatientEnded, reloaded.EndReason);
        Assert.False(reloaded.IsActiveAt(Start.AddMinutes(21)));
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAnotherRequestEndedTheGrantFirst_ThrowsAndKeepsTheFirstEnd()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        GrantScope scope;
        await using (var seedDb = new MediPassDbContext(options))
        {
            await seedDb.Database.EnsureCreatedAsync();
            scope = await SeedGrantAsync(seedDb);
        }

        var visitScope = new VisitAccessScope(scope.AppointmentId, scope.DoctorId, scope.PatientId);

        await using var firstDb = new MediPassDbContext(options);
        await using var secondDb = new MediPassDbContext(options);
        var firstStore = new AccessGrantEndStore(firstDb);
        var secondStore = new AccessGrantEndStore(secondDb);

        var first = await firstStore.LoadAsync(scope.GrantId, visitScope);
        var second = await secondStore.LoadAsync(scope.GrantId, visitScope);
        Assert.NotNull(first);
        Assert.NotNull(second);

        // The second request commits its end first. The first request's view is now stale.
        second.EndByPatient(Start.AddMinutes(20));
        await secondStore.SaveChangesAsync();

        first.EndByPatient(Start.AddMinutes(25));
        await Assert.ThrowsAsync<AccessGrantConflictException>(() => firstStore.SaveChangesAsync());

        await using var reloadedDb = new MediPassDbContext(options);
        var reloaded = await reloadedDb.AccessGrants.AsNoTracking()
            .SingleAsync(grant => grant.Id == scope.GrantId);

        // The first end stands: the stale writer never overwrites it (FR-ACS-09, FR-ACS-11).
        Assert.Equal(Start.AddMinutes(20), reloaded.EndedAtUtc);
        Assert.Equal(GrantEndReason.PatientEnded, reloaded.EndReason);
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
            Status = AppointmentStatus.InVisit,
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

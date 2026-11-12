using MediPassPlus.Application.AppointmentStatusFlow;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Tests.AppointmentStatusFlow;

public class AppointmentStatusStoreTests
{
    private static readonly DateTime Start = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task LoadForCompleteAsync_ReturnsTheAppointmentsGrant()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var doctorUserId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var grant = await SeedVisitAsync(db, doctorUserId, patientId);
        var store = new AppointmentStatusStore(db);

        var context = await store.LoadForCompleteAsync(42, doctorUserId);

        Assert.NotNull(context);
        Assert.Equal(42, context.Appointment.Id);
        Assert.NotNull(context.Doctor);
        Assert.Equal(7, context.Doctor.Id);
        Assert.Same(grant, context.Grant);
    }

    [Fact]
    public async Task LoadForCompleteAsync_WhenTheVisitHasNoGrant_ReturnsNoGrant()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var doctorUserId = Guid.NewGuid();
        await SeedVisitAsync(db, doctorUserId, Guid.NewGuid(), createGrant: false);
        var store = new AppointmentStatusStore(db);

        var context = await store.LoadForCompleteAsync(42, doctorUserId);

        Assert.NotNull(context);
        Assert.Null(context.Grant);
    }

    [Fact]
    public async Task LoadForCompleteAsync_ForAnUnknownAppointment_ReturnsNull()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var store = new AppointmentStatusStore(db);

        var context = await store.LoadForCompleteAsync(42, Guid.NewGuid());

        Assert.Null(context);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenThePatientEndedTheGrantFirst_ThrowsAndLeavesTheVisitInVisit()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        var doctorUserId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        Guid grantId;
        await using (var seedDb = new MediPassDbContext(options))
        {
            await seedDb.Database.EnsureCreatedAsync();
            var grant = await SeedVisitAsync(seedDb, doctorUserId, patientId);
            Assert.NotNull(grant);
            grantId = grant.Id;
        }

        // The doctor loads the visit and its active grant, then the patient ends the grant before the save.
        await using var completionDb = new MediPassDbContext(options);
        await using var patientDb = new MediPassDbContext(options);
        var store = new AppointmentStatusStore(completionDb);

        var context = await store.LoadForCompleteAsync(42, doctorUserId);
        Assert.NotNull(context);
        Assert.NotNull(context.Grant);
        Assert.True(context.Appointment.TryComplete(Start.AddMinutes(10)));
        context.Grant.CompleteVisit(Start.AddMinutes(10));

        var patientGrant = await patientDb.AccessGrants
            .SingleAsync(grant => grant.Id == grantId);
        patientGrant.EndByPatient(Start.AddMinutes(11));
        await patientDb.SaveChangesAsync();

        await Assert.ThrowsAsync<AppointmentStatusConflictException>(() => store.SaveChangesAsync());

        await using var reloadedDb = new MediPassDbContext(options);
        var storedGrant = await reloadedDb.AccessGrants.AsNoTracking()
            .SingleAsync(grant => grant.Id == grantId);
        var storedAppointment = await reloadedDb.Appointments.AsNoTracking()
            .SingleAsync(appointment => appointment.Id == 42);

        // The patient end stands and nothing from the stale completion commits: the whole save rolled back.
        Assert.Equal(Start.AddMinutes(11), storedGrant.EndedAtUtc);
        Assert.Equal(GrantEndReason.PatientEnded, storedGrant.EndReason);
        Assert.Equal(AppointmentStatus.InVisit, storedAppointment.Status);
        Assert.Null(storedAppointment.CompletedAtUtc);
    }

    private static async Task<AccessGrant?> SeedVisitAsync(
        MediPassDbContext db,
        Guid doctorUserId,
        Guid patientId,
        bool createGrant = true)
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

        db.Appointments.Add(appointment);

        AccessGrant? grant = null;
        if (createGrant)
        {
            var doctor = db.Doctors.Local.Single(candidate => candidate.Id == 7);
            var request = AccessRequest.Create(appointment, doctor, Start.AddMinutes(-5));
            request.Approve();

            grant = AccessGrant.Create(request, appointment, Start);
            db.AccessRequests.Add(request);
            db.AccessGrants.Add(grant);
        }

        await db.SaveChangesAsync();

        return grant;
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

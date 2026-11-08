using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Tests.VisitAccess;

public class StartVisitStoreTests
{
    private static readonly DateTime Start = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task LoadAsync_ReturnsTheScopedAppointmentRequestsAndLiveQrCredentials()
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
        var pending = SeedVisit(db, doctorUserId, patientId);
        var live = VisitAccessCredential.Create(
            pending.Id,
            VisitCredentialType.QrToken,
            "live-hash",
            Start);
        var revoked = VisitAccessCredential.Create(
            pending.Id,
            VisitCredentialType.QrToken,
            "revoked-hash",
            Start.AddMinutes(-3));
        revoked.Revoke(Start.AddMinutes(-2));

        db.VisitAccessCredentials.AddRange(live, revoked);
        await db.SaveChangesAsync();

        var store = new StartVisitStore(db);

        var context = await store.LoadAsync(42, 7, patientId);

        Assert.NotNull(context);
        Assert.Equal(42, context.Appointment.Id);
        Assert.NotNull(context.Appointment.DoctorSlot);
        Assert.Equal(7, context.Doctor.Id);
        var request = Assert.Single(context.Requests);
        Assert.Equal(pending.Id, request.Id);
        var credential = Assert.Single(context.LiveQrCredentials);
        Assert.Equal(live.Id, credential.Id);
    }

    [Fact]
    public async Task LoadAsync_ForAnotherDoctorsScope_ReturnsNull()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var patientId = Guid.NewGuid();
        SeedVisit(db, Guid.NewGuid(), patientId);
        await db.SaveChangesAsync();

        var store = new StartVisitStore(db);

        var context = await store.LoadAsync(42, doctorId: 8, patientId);

        Assert.Null(context);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenASecondPendingRequestIsStaged_ThrowsPendingRequestConflict()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var patientId = Guid.NewGuid();
        SeedVisit(db, Guid.NewGuid(), patientId);
        await db.SaveChangesAsync();

        var appointment = await db.Appointments.SingleAsync();
        var doctor = await db.Doctors.SingleAsync();
        db.AccessRequests.Add(AccessRequest.Create(appointment, doctor, Start.AddMinutes(-1)));

        var store = new StartVisitStore(db);

        var exception = await Assert.ThrowsAsync<PendingRequestConflictException>(
            () => store.SaveChangesAsync());

        Assert.NotNull(exception.InnerException);
    }

    private static AccessRequest SeedVisit(
        MediPassDbContext db,
        Guid doctorUserId,
        Guid patientId)
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
            Status = AppointmentStatus.CheckedIn,
            CreatedAtUtc = Start.AddDays(-1)
        };
        var doctor = db.Doctors.Local.Single(doctor => doctor.Id == 7);
        var request = AccessRequest.Create(appointment, doctor, Start.AddMinutes(-5));

        db.Appointments.Add(appointment);
        db.AccessRequests.Add(request);

        return request;
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

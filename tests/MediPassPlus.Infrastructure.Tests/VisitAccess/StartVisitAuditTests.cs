using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Tests.VisitAccess;

public class StartVisitAuditTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartAsync_PersistsTheNewRequestAndItsAccessRequestCreatedAuditRowTogether()
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
        Seed(db, doctorUserId, patientId);
        await db.SaveChangesAsync();

        var timeProvider = new FixedTimeProvider(Now);
        var service = new StartVisitService(
            new VisitAccessScopeService(new VisitAccessScopeStore(db)),
            new StartVisitStore(db),
            new AuditWriter(db, timeProvider),
            timeProvider);

        var result = await service.StartAsync(42, doctorUserId, "203.0.113.10");

        Assert.True(result.IsSuccess);

        var request = await db.AccessRequests.SingleAsync();
        var audit = await db.AuditLogs.SingleAsync();
        Assert.Equal(AuditAction.AccessRequestCreated, audit.Action);
        Assert.Equal(AuditEntityType.AccessRequest, audit.EntityType);
        Assert.Equal(request.Id.ToString(), audit.EntityId);
        Assert.Equal(doctorUserId, audit.UserId);
        Assert.Equal(patientId, audit.PatientId);
        Assert.Equal("203.0.113.10", audit.IpAddress);
        Assert.Equal(Now, audit.OccurredAtUtc);
        Assert.Null(audit.AccessGrantId);

        var credential = await db.VisitAccessCredentials.SingleAsync();
        Assert.Equal(request.Id, credential.AccessRequestId);
        Assert.Equal(QrTokenGenerator.Hash(result.Response!.QrToken), credential.SecretHash);
        Assert.NotEqual(result.Response.QrToken, credential.SecretHash);
        Assert.Equal(Now.UtcDateTime, credential.CreatedAtUtc);
        Assert.Equal(Now.UtcDateTime.AddMinutes(2), credential.ExpiresAtUtc);
    }

    private static void Seed(MediPassDbContext db, Guid doctorUserId, Guid patientId)
    {
        var start = Now.UtcDateTime.Date.AddHours(9);
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
            StartTimeUtc = start,
            EndTimeUtc = start.AddMinutes(30)
        });
        db.Appointments.Add(new Appointment
        {
            Id = 42,
            PatientId = patientId,
            DoctorId = 7,
            DoctorSlotId = 1,
            Status = AppointmentStatus.CheckedIn,
            CreatedAtUtc = start.AddDays(-1)
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

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

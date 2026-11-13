using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Tests.VisitAccess;

public class QrScanPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AccessRequestScanStore_ResolvesTheTokenToItsRequestDoctorAndClinic()
    {
        await using var database = await TestDatabase.CreateAsync();
        var request = database.CreatePendingRequest(database.Context, Now.UtcDateTime.AddMinutes(-5));
        var credential = database.AddCredential(
            database.Context,
            request,
            VisitCredentialType.QrToken,
            "stored-qr-hash");
        await database.Context.SaveChangesAsync();

        var store = new AccessRequestScanStore(database.Context);
        var context = await store.FindByTokenHashAsync("stored-qr-hash");

        Assert.NotNull(context);
        Assert.Equal(credential.Id, context.Credential.Id);
        Assert.Equal(request.Id, context.Request.Id);
        Assert.Equal(42, context.Appointment.Id);
        Assert.Equal("Doctor User", context.DoctorName);
        Assert.Equal("General", context.DoctorSpecialty);
        Assert.Equal("Test clinic", context.ClinicName);
        Assert.Equal("Test address", context.ClinicAddress);
        Assert.Equal(Now.UtcDateTime.Date.AddHours(9), context.AppointmentStartsAtUtc);
    }

    [Fact]
    public async Task AccessRequestScanStore_IgnoresANumericCodeCredentialWithTheSameHash()
    {
        await using var database = await TestDatabase.CreateAsync();
        var request = database.CreatePendingRequest(database.Context, Now.UtcDateTime.AddMinutes(-5));
        database.AddCredential(
            database.Context,
            request,
            VisitCredentialType.NumericCode,
            "shared-hash");
        await database.Context.SaveChangesAsync();

        var store = new AccessRequestScanStore(database.Context);
        var context = await store.FindByTokenHashAsync("shared-hash");

        Assert.Null(context);
    }

    [Fact]
    public async Task AccessRequestScanStore_WhenNoHashMatches_ReturnsNull()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new AccessRequestScanStore(database.Context);

        Assert.Null(await store.FindByTokenHashAsync("unknown-hash"));
    }

    [Fact]
    public async Task AccessStateStore_ReturnsTheNewestRequestAndTheAppointmentGrant()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Context;

        var older = database.CreatePendingRequest(db, Now.UtcDateTime.AddMinutes(-20));
        older.Reject();
        var newest = database.CreatePendingRequest(db, Now.UtcDateTime.AddMinutes(-5));
        newest.Approve();
        var grant = AccessGrant.Create(newest, database.Appointment, Now.UtcDateTime.AddMinutes(-5));

        db.AccessGrants.Add(grant);
        await db.SaveChangesAsync();

        var store = new AccessStateStore(db);
        var context = await store.LoadAsync(42, database.Doctor.Id, database.PatientId);

        Assert.NotNull(context);
        Assert.Equal(newest.Id, context.Request!.Id);
        Assert.Equal(grant.Id, context.Grant!.Id);
    }

    [Fact]
    public async Task AccessStateStore_WhenTheAppointmentHasNoRequestOrGrant_ReturnsAnEmptyContext()
    {
        await using var database = await TestDatabase.CreateAsync();

        var store = new AccessStateStore(database.Context);
        var context = await store.LoadAsync(42, database.Doctor.Id, database.PatientId);

        Assert.NotNull(context);
        Assert.Null(context.Request);
        Assert.Null(context.Grant);
    }

    [Fact]
    public async Task AccessStateStore_WhenThePatientDoesNotOwnTheAppointment_ReturnsNull()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new AccessStateStore(database.Context);

        var context = await store.LoadAsync(42, database.Doctor.Id, Guid.NewGuid());

        Assert.Null(context);
    }

    [Fact]
    public async Task AccessStateStore_WhenTheDoctorDoesNotOwnTheAppointment_ReturnsNull()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new AccessStateStore(database.Context);

        var context = await store.LoadAsync(42, database.Doctor.Id + 1, database.PatientId);

        Assert.Null(context);
    }

    [Fact]
    public async Task AccessRequestDecisionStore_ResolvesTheTokenAndItsDecisionContext()
    {
        await using var database = await TestDatabase.CreateAsync();
        var request = database.CreatePendingRequest(database.Context, Now.UtcDateTime.AddMinutes(-5));
        var credential = database.AddCredential(
            database.Context,
            request,
            VisitCredentialType.QrToken,
            "decision-qr-hash");
        await database.Context.SaveChangesAsync();

        var store = new AccessRequestDecisionStore(database.Context);
        var context = await store.FindByTokenHashAsync("decision-qr-hash");

        Assert.NotNull(context);
        Assert.Equal(credential.Id, context.Credential.Id);
        Assert.Equal(request.Id, context.Decision.Request.Id);
        Assert.Equal(42, context.Decision.Appointment.Id);
        Assert.Contains(context.Decision.Credentials, item => item.Id == credential.Id);
    }

    [Fact]
    public async Task AccessRequestDecisionStore_IgnoresANumericCodeCredentialWithTheSameHash()
    {
        await using var database = await TestDatabase.CreateAsync();
        var request = database.CreatePendingRequest(database.Context, Now.UtcDateTime.AddMinutes(-5));
        database.AddCredential(
            database.Context,
            request,
            VisitCredentialType.NumericCode,
            "decision-shared-hash");
        await database.Context.SaveChangesAsync();

        var store = new AccessRequestDecisionStore(database.Context);

        Assert.Null(await store.FindByTokenHashAsync("decision-shared-hash"));
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly DbContextOptions<MediPassDbContext> _options;

        private TestDatabase(
            SqliteConnection connection,
            DbContextOptions<MediPassDbContext> options,
            MediPassDbContext context,
            Doctor doctor,
            Appointment appointment,
            Guid patientId)
        {
            Connection = connection;
            _options = options;
            Context = context;
            Doctor = doctor;
            Appointment = appointment;
            PatientId = patientId;
        }

        public SqliteConnection Connection { get; }

        public MediPassDbContext Context { get; }

        public Doctor Doctor { get; }

        public Appointment Appointment { get; }

        public Guid PatientId { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<MediPassDbContext>()
                .UseSqlite(connection)
                .Options;

            var db = new MediPassDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var doctorUserId = Guid.NewGuid();
            var patientId = Guid.NewGuid();
            Seed(db, doctorUserId, patientId);
            await db.SaveChangesAsync();

            return new TestDatabase(
                connection,
                options,
                db,
                await db.Doctors.AsNoTracking().SingleAsync(),
                await db.Appointments.AsNoTracking().SingleAsync(),
                patientId);
        }

        public AccessRequest CreatePendingRequest(MediPassDbContext db, DateTime createdAtUtc)
        {
            var request = AccessRequest.Create(Appointment, Doctor, createdAtUtc);
            db.AccessRequests.Add(request);
            return request;
        }

        public VisitAccessCredential AddCredential(
            MediPassDbContext db,
            AccessRequest request,
            VisitCredentialType type,
            string secretHash)
        {
            var credential = VisitAccessCredential.Create(
                request.Id,
                type,
                secretHash,
                Now.UtcDateTime.AddMinutes(-1));
            db.VisitAccessCredentials.Add(credential);
            return credential;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
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
    }
}

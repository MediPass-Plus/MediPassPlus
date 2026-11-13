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

public class AccessRequestDecisionPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
    private const string IpAddress = "203.0.113.10";

    [Fact]
    public async Task ApproveAsync_PersistsTheGrantScopeAndTheAccessGrantCreatedAuditRow()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Context;
        var request = database.CreatePendingRequest(db);
        var credential = database.AddCredential(db, request);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.ApproveAsync(request.Id, database.PatientId, IpAddress);

        Assert.True(result.IsSuccess);
        var response = Assert.IsType<AccessGrantResponse>(result.Grant);

        var grant = await db.AccessGrants.AsNoTracking().SingleAsync();
        Assert.Equal(response.GrantId, grant.Id);
        Assert.Equal(request.Id, grant.AccessRequestId);
        Assert.Equal(42, grant.AppointmentId);
        Assert.Equal(7, grant.DoctorId);
        Assert.Equal(database.PatientId, grant.PatientId);
        Assert.Equal(Now.UtcDateTime, grant.GrantedAtUtc);
        Assert.Equal(Now.UtcDateTime.AddHours(2), grant.ExpiresAtUtc);
        Assert.Null(grant.EndedAtUtc);
        Assert.Null(grant.EndReason);

        var storedRequest = await db.AccessRequests.AsNoTracking().SingleAsync();
        Assert.Equal(AccessRequestStatus.Approved, storedRequest.Status);

        var storedCredential = await db.VisitAccessCredentials.AsNoTracking().SingleAsync();
        Assert.Equal(Now.UtcDateTime, storedCredential.ConsumedAtUtc);
        Assert.Null(storedCredential.RevokedAtUtc);
        Assert.False(storedCredential.IsUsableAt(Now.UtcDateTime));

        var audit = await db.AuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(AuditAction.AccessGrantCreated, audit.Action);
        Assert.Equal(AuditEntityType.AccessGrant, audit.EntityType);
        Assert.Equal(grant.Id.ToString(), audit.EntityId);
        Assert.Equal(grant.Id, audit.AccessGrantId);
        Assert.Equal(database.PatientId, audit.UserId);
        Assert.Equal(database.PatientId, audit.PatientId);
        Assert.Equal(IpAddress, audit.IpAddress);
        Assert.Equal(Now, audit.OccurredAtUtc);
    }

    [Fact]
    public async Task RejectAsync_PersistsTheRejectionAndRevokesTheCredentialWithoutCreatingAGrant()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Context;
        var request = database.CreatePendingRequest(db);
        var credential = database.AddCredential(db, request);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.RejectAsync(request.Id, database.PatientId, IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Grant);

        Assert.Empty(await db.AccessGrants.AsNoTracking().ToListAsync());
        var storedRequest = await db.AccessRequests.AsNoTracking().SingleAsync();
        Assert.Equal(AccessRequestStatus.Rejected, storedRequest.Status);

        var storedCredential = await db.VisitAccessCredentials.AsNoTracking().SingleAsync();
        Assert.Equal(Now.UtcDateTime, storedCredential.RevokedAtUtc);
        Assert.Null(storedCredential.ConsumedAtUtc);

        var audit = await db.AuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(AuditAction.AccessRequestRejected, audit.Action);
        Assert.Equal(AuditEntityType.AccessRequest, audit.EntityType);
        Assert.Equal(request.Id.ToString(), audit.EntityId);
        Assert.Null(audit.AccessGrantId);
        Assert.Equal(database.PatientId, audit.UserId);
        Assert.Equal(database.PatientId, audit.PatientId);
        Assert.Equal(IpAddress, audit.IpAddress);
    }

    [Fact]
    public async Task ApproveAsync_WhenTheRequestWasAlreadyApproved_ReturnsAlreadyAnsweredWithoutWritingAgain()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Context;
        var request = database.CreatePendingRequest(db);
        database.AddCredential(db, request);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var first = await service.ApproveAsync(request.Id, database.PatientId, IpAddress);
        var second = await service.ApproveAsync(request.Id, database.PatientId, IpAddress);

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.AlreadyAnswered, second.Failure);
        Assert.Equal(1, await db.AccessGrants.AsNoTracking().CountAsync());
        Assert.Equal(1, await db.AuditLogs.AsNoTracking().CountAsync());
        Assert.Equal(
            AccessRequestStatus.Approved,
            (await db.AccessRequests.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task RejectAsync_WhenTheRequestWasAlreadyRejected_ReturnsAlreadyAnsweredWithoutWritingAgain()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Context;
        var request = database.CreatePendingRequest(db);
        database.AddCredential(db, request);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var first = await service.RejectAsync(request.Id, database.PatientId, IpAddress);
        var second = await service.RejectAsync(request.Id, database.PatientId, IpAddress);

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.AlreadyAnswered, second.Failure);
        Assert.Equal(1, await db.AuditLogs.AsNoTracking().CountAsync());
        Assert.Empty(await db.AccessGrants.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ApproveAsync_WhenTheAppointmentAlreadyHasAGrant_ReturnsRequestConflictAndRollsBack()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Context;

        var earlier = AccessRequest.Create(
            database.Appointment,
            database.Doctor,
            Now.UtcDateTime.AddMinutes(-10));
        earlier.Approve();
        var existingGrant = AccessGrant.Create(
            earlier,
            database.Appointment,
            Now.UtcDateTime.AddMinutes(-10));
        var pending = database.CreatePendingRequest(db);
        var credential = database.AddCredential(db, pending);

        db.AccessRequests.Add(earlier);
        db.AccessGrants.Add(existingGrant);
        await db.SaveChangesAsync();

        // Seeding shares this context; in production the grant was persisted by an earlier request. Detach the
        // seeded graph so adding a second grant is decided by the database guard, not by EF's one-to-one fixup.
        db.ChangeTracker.Clear();

        var service = CreateService(db);
        var result = await service.ApproveAsync(pending.Id, database.PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.RequestConflict, result.Failure);

        Assert.Equal(1, await db.AccessGrants.AsNoTracking().CountAsync());
        Assert.Equal(
            AccessRequestStatus.Pending,
            (await db.AccessRequests.AsNoTracking().SingleAsync(r => r.Id == pending.Id)).Status);
        Assert.Empty(await db.AuditLogs.AsNoTracking().ToListAsync());

        // The losing write must not consume the credential either.
        var storedCredential = await db.VisitAccessCredentials.AsNoTracking().SingleAsync();
        Assert.Equal(credential.Id, storedCredential.Id);
        Assert.Null(storedCredential.ConsumedAtUtc);
        Assert.Null(storedCredential.RevokedAtUtc);
    }

    [Fact]
    public async Task ApproveAsync_WhenAConcurrentDecisionApprovedTheRequest_ReturnsRequestConflictAndKeepsTheWinner()
    {
        await using var database = await TestDatabase.CreateAsync();
        var db = database.Context;
        var request = database.CreatePendingRequest(db);
        database.AddCredential(db, request);
        await db.SaveChangesAsync();

        await using var competing = database.CreateContext();
        var innerStore = new AccessRequestDecisionStore(db);
        var interleavingStore = new InterleavingDecisionStore(
            innerStore,
            async () =>
            {
                // Simulate the other request committing its decision after this request loaded the row but before
                // it wrote its own. The Status concurrency token must reject the second write.
                var competingRequest = await competing.AccessRequests
                    .SingleAsync(r => r.Id == request.Id);
                competingRequest.Approve();
                await competing.SaveChangesAsync();
            });
        var timeProvider = new FixedTimeProvider(Now);
        var service = new AccessRequestDecisionService(
            new VisitAccessScopeService(new VisitAccessScopeStore(db)),
            interleavingStore,
            new AuditWriter(db, timeProvider),
            timeProvider);

        var result = await service.ApproveAsync(request.Id, database.PatientId, IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(AccessRequestDecisionFailure.RequestConflict, result.Failure);

        await using var verify = database.CreateContext();
        Assert.Empty(await verify.AccessGrants.ToListAsync());
        Assert.Empty(await verify.AuditLogs.ToListAsync());
        Assert.Equal(
            AccessRequestStatus.Approved,
            (await verify.AccessRequests.SingleAsync()).Status);
    }

    private static AccessRequestDecisionService CreateService(MediPassDbContext db)
    {
        var timeProvider = new FixedTimeProvider(Now);

        return new AccessRequestDecisionService(
            new VisitAccessScopeService(new VisitAccessScopeStore(db)),
            new AccessRequestDecisionStore(db),
            new AuditWriter(db, timeProvider),
            timeProvider);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class InterleavingDecisionStore : IAccessRequestDecisionStore
    {
        private readonly IAccessRequestDecisionStore _inner;
        private readonly Func<Task> _beforeSave;

        public InterleavingDecisionStore(IAccessRequestDecisionStore inner, Func<Task> beforeSave)
        {
            _inner = inner;
            _beforeSave = beforeSave;
        }

        public Task<AccessRequest?> FindRequestAsync(
            Guid requestId,
            CancellationToken cancellationToken = default) =>
            _inner.FindRequestAsync(requestId, cancellationToken);

        public Task<AccessRequestDecisionContext?> LoadAsync(
            Guid requestId,
            VisitAccessScope scope,
            CancellationToken cancellationToken = default) =>
            _inner.LoadAsync(requestId, scope, cancellationToken);

        public Task<AccessRequestTokenContext?> FindByTokenHashAsync(
            string tokenHash,
            CancellationToken cancellationToken = default) =>
            _inner.FindByTokenHashAsync(tokenHash, cancellationToken);

        public void AddGrant(AccessGrant grant) => _inner.AddGrant(grant);

        public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await _beforeSave();
            await _inner.SaveChangesAsync(cancellationToken);
        }
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

        public MediPassDbContext CreateContext() => new(_options);

        public AccessRequest CreatePendingRequest(MediPassDbContext db)
        {
            var request = AccessRequest.Create(
                Appointment,
                Doctor,
                Now.UtcDateTime.AddMinutes(-5));
            db.AccessRequests.Add(request);
            return request;
        }

        public VisitAccessCredential AddCredential(MediPassDbContext db, AccessRequest request)
        {
            var credential = VisitAccessCredential.Create(
                request.Id,
                VisitCredentialType.QrToken,
                "qr-hash-" + Guid.NewGuid().ToString("N"),
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

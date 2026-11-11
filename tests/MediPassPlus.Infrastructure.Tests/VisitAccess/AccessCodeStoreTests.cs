using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Tests.VisitAccess;

public class AccessCodeStoreTests
{
    private static readonly DateTime Start = new(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] HmacKey =
        Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public async Task LoadAsync_ReturnsTheScopedAppointmentRequestsAndCredentials()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var graph = await SeedGraphAsync(db, includeCode: true, includeQr: true);
        var store = new AccessCodeStore(db);

        var context = await store.LoadAsync(
            graph.AppointmentId,
            graph.DoctorId,
            graph.PatientId,
            CancellationToken.None);

        Assert.NotNull(context);
        Assert.Equal(graph.AppointmentId, context.Appointment.Id);
        Assert.NotNull(context.Appointment.DoctorSlot);
        var request = Assert.Single(context.Requests);
        Assert.Equal(graph.RequestId, request.Id);
        Assert.Equal(2, context.Credentials.Count);
        Assert.Contains(context.Credentials, credential => credential.Type == VisitCredentialType.NumericCode);
        Assert.Contains(context.Credentials, credential => credential.Type == VisitCredentialType.QrToken);
    }

    [Fact]
    public async Task LoadAsync_ForAnotherDoctorOrPatient_ReturnsNull()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var graph = await SeedGraphAsync(db, includeCode: false, includeQr: false);
        var store = new AccessCodeStore(db);

        Assert.Null(await store.LoadAsync(
            graph.AppointmentId,
            graph.DoctorId + 1,
            graph.PatientId,
            CancellationToken.None));
        Assert.Null(await store.LoadAsync(
            graph.AppointmentId,
            graph.DoctorId,
            Guid.NewGuid(),
            CancellationToken.None));
    }

    [Fact]
    public async Task SaveChangesAsync_WhenASecondLiveCodeIsInserted_MapsTheUniqueIndexToAConflict()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var graph = await SeedGraphAsync(db, includeCode: true, includeQr: false);
        var store = new AccessCodeStore(db);
        var duplicate = VisitAccessCredential.Create(
            graph.RequestId,
            VisitCredentialType.NumericCode,
            new HmacAccessCodeHasher(HmacKey).Hash("123456"),
            Start.AddMinutes(1));
        store.AddCredential(duplicate);

        await Assert.ThrowsAsync<AccessCodeConflictException>(
            () => store.SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTheAppointmentAlreadyHasAGrant_MapsTheUniqueIndexToAConflict()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        Graph graph;
        await using (var seedDb = new MediPassDbContext(options))
        {
            await seedDb.Database.EnsureCreatedAsync();

            graph = await SeedGraphAsync(seedDb, includeCode: false, includeQr: false);
            var firstRequest = await seedDb.AccessRequests
                .SingleAsync(candidate => candidate.Id == graph.RequestId);
            firstRequest.Approve();
            await seedDb.SaveChangesAsync();

            var firstAppointment = await seedDb.Appointments
                .SingleAsync(candidate => candidate.Id == graph.AppointmentId);
            seedDb.AccessGrants.Add(AccessGrant.Create(firstRequest, firstAppointment, Start));
            await seedDb.SaveChangesAsync();
        }

        // A second context stands in for the concurrent request: its first grant is already committed, so a second
        // grant for the same appointment must be rejected by the one-grant-per-appointment index (FR-ACS-11).
        await using var db = new MediPassDbContext(options);
        var appointment = await db.Appointments.SingleAsync(candidate => candidate.Id == graph.AppointmentId);
        var doctor = await db.Doctors.SingleAsync(candidate => candidate.Id == graph.DoctorId);
        var secondRequest = AccessRequest.Create(appointment, doctor, Start.AddMinutes(1));
        secondRequest.Approve();
        db.AccessRequests.Add(secondRequest);
        await db.SaveChangesAsync();

        var store = new AccessCodeStore(db);
        store.AddGrant(AccessGrant.Create(secondRequest, appointment, Start.AddMinutes(2)));

        await Assert.ThrowsAsync<AccessCodeConflictException>(
            () => store.SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveChangesAsync_StoresOnlyTheHmacOfTheGeneratedCode()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var graph = await SeedGraphAsync(db, includeCode: false, includeQr: false);
        var hasher = new HmacAccessCodeHasher(HmacKey);
        var code = "042915";
        var store = new AccessCodeStore(db);
        store.AddCredential(VisitAccessCredential.Create(
            graph.RequestId,
            VisitCredentialType.NumericCode,
            hasher.Hash(code),
            Start));

        await store.SaveChangesAsync(CancellationToken.None);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SecretHash FROM VisitAccessCredentials;";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        var storedSecret = reader.GetString(0);
        Assert.Equal(hasher.Hash(code), storedSecret);
        Assert.NotEqual(code, storedSecret);
        Assert.DoesNotContain(code, storedSecret, StringComparison.Ordinal);
        Assert.False(await reader.ReadAsync());

        await reader.DisposeAsync();

        await using var columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = "SELECT name FROM pragma_table_info('VisitAccessCredentials');";
        await using var columnsReader = await columnsCommand.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await columnsReader.ReadAsync())
        {
            columns.Add(columnsReader.GetString(0));
        }

        Assert.Contains("SecretHash", columns);
        Assert.DoesNotContain("Code", columns);
        Assert.DoesNotContain("PlaintextCode", columns);
    }

    private static async Task<Graph> SeedGraphAsync(
        MediPassDbContext db,
        bool includeCode,
        bool includeQr)
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
        db.Appointments.Add(appointment);

        var doctor = db.Doctors.Local.Single(candidate => candidate.Id == 7);
        var request = AccessRequest.Create(appointment, doctor, Start.AddMinutes(-5));
        db.AccessRequests.Add(request);

        var hasher = new HmacAccessCodeHasher(HmacKey);
        if (includeCode)
        {
            db.VisitAccessCredentials.Add(VisitAccessCredential.Create(
                request.Id,
                VisitCredentialType.NumericCode,
                hasher.Hash("042915"),
                Start.AddMinutes(-1)));
        }

        if (includeQr)
        {
            db.VisitAccessCredentials.Add(VisitAccessCredential.Create(
                request.Id,
                VisitCredentialType.QrToken,
                QrTokenGenerator.Hash("test-qr-token"),
                Start.AddMinutes(-1)));
        }

        await db.SaveChangesAsync();

        return new Graph(appointment.Id, doctor.Id, patientId, request.Id);
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

    private sealed record Graph(int AppointmentId, int DoctorId, Guid PatientId, Guid RequestId);
}

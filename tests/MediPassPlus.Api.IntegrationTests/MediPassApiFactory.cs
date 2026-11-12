using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using MediPassPlus.Application.Common;
using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.StaffAdmin;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Infrastructure.Extensions;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// Hosts the real API over HTTP with a SQL Server database (ADR-0006).
/// Testcontainers SQL Server is the default. Set <c>MEDIPASS_TEST_SQLSERVER</c> to a SQL Server
/// connection string to run locally without Docker; the provider stays SQL Server, so authorization
/// and Identity behavior are exercised through the same code path as production.
/// </summary>
public sealed class MediPassApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string JwtIssuer = "MediPassPlus";
    private const string JwtAudience = "MediPassPlus.Client";
    private const string TestPassword = "Integration1!";
    private const string LocalSqlServerVariable = "MEDIPASS_TEST_SQLSERVER";

    private static readonly IClinicTimeZone ClinicTimeZone = new CairoClinicTimeZone();

    // Generated at runtime: the test signing key is never committed (NFR-SEC-08).
    private static readonly string JwtSecretKey =
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    // A separate runtime access-code HMAC key; it must never equal the JWT key (ADR-0007 decision 5).
    private static readonly string AccessCodeHmacKey =
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    private static readonly IAccessCodeHasher AccessCodeHasher =
        new HmacAccessCodeHasher(Convert.FromBase64String(AccessCodeHmacKey));

    private readonly string _connectionString;
    private readonly string _databaseName;
    private readonly MsSqlContainer? _sqlContainer;
    private readonly Dictionary<string, string?> _previousEnvironment = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _tokens = new(StringComparer.Ordinal);
    private bool _cleanedUp;

    /// <summary>
    /// Starts the database server when no local SQL Server is configured, creates this run's isolated database and
    /// applies every migration.
    /// </summary>
    public MediPassApiFactory()
    {
        var serverConnectionString = Environment.GetEnvironmentVariable(LocalSqlServerVariable);

        if (string.IsNullOrWhiteSpace(serverConnectionString))
        {
            _sqlContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            _sqlContainer.StartAsync().GetAwaiter().GetResult();
            serverConnectionString = _sqlContainer.GetConnectionString();
        }

        _databaseName = $"MediPassPlus_IntegrationTests_{Guid.NewGuid():N}";
        _connectionString = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = _databaseName
        }.ConnectionString;

        // Program reads Jwt and ConnectionStrings before builder.Build(), so the deferred
        // WebApplicationFactory configuration would arrive too late. Environment variables are
        // part of WebApplication.CreateBuilder's configuration from the start.
        SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        SetEnvironmentVariable("Jwt__Issuer", JwtIssuer);
        SetEnvironmentVariable("Jwt__Audience", JwtAudience);
        SetEnvironmentVariable("Jwt__SecretKey", JwtSecretKey);
        SetEnvironmentVariable("Jwt__AccessTokenMinutes", "15");
        SetEnvironmentVariable("AccessCode__HmacKey", AccessCodeHmacKey);
        SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _connectionString);
        SetEnvironmentVariable("OpenApi__ExposeOutsideDevelopment", "true");
        SetEnvironmentVariable("Logging__LogLevel__Microsoft.EntityFrameworkCore", "Warning");

        ApplyMigrations();
    }

    /// <summary>
    /// Creates (or reuses) a test user in the requested role and returns a signed JWT for it.
    /// Tokens are minted by the application's own <see cref="JwtTokenService"/>, so the test client
    /// exercises the real bearer authentication and role checks.
    /// </summary>
    public async Task<string> CreateTokenForRoleAsync(string role)
    {
        if (_tokens.TryGetValue(role, out var existingToken))
        {
            return existingToken;
        }

        using var scope = Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var jwt = scope.ServiceProvider.GetRequiredService<JwtTokenService>();

        if (!await roleManager.RoleExistsAsync(role))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create role '{role}': {string.Join("; ", roleResult.Errors.Select(e => e.Description))}");
            }
        }

        var email = $"{role.ToLowerInvariant()}@integration.medipass.test";
        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = $"{role} Integration Test",
                Role = role,
                IsActive = true,
                EmailConfirmed = true
            };

            var userResult = await userManager.CreateAsync(user, TestPassword);
            if (!userResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create '{email}': {string.Join("; ", userResult.Errors.Select(e => e.Description))}");
            }

            var roleAssignment = await userManager.AddToRoleAsync(user, role);
            if (!roleAssignment.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not add '{email}' to '{role}': {string.Join("; ", roleAssignment.Errors.Select(e => e.Description))}");
            }
        }

        var token = jwt.CreateAccessToken(user, new[] { role });
        _tokens[role] = token;
        return token;
    }

    /// <summary>The clinic-local date in Africa/Cairo, used to seed and query "today" scenarios.</summary>
    public DateOnly ClinicToday => ClinicTimeZone.GetLocalDate(DateTime.UtcNow);

    /// <summary>Computes the HMAC the API stores for a six-digit code, so a test can seed a known code.</summary>
    /// <param name="code">The plaintext code.</param>
    /// <returns>The Base64 HMAC-SHA-256 hash.</returns>
    public string HashAccessCode(string code) => AccessCodeHasher.Hash(code);

    /// <summary>Creates a unique account in the requested role and mints a bearer token for it, so each test can use its own
    /// patient, doctor or other actor without sharing rate-limit partitions.</summary>
    /// <param name="role">The application role to assign.</param>
    /// <param name="fullName">An optional display name; defaults to the role-based test name.</param>
    /// <returns>The new account and its signed token.</returns>
    public async Task<TestUser> CreateUserAsync(string role, string? fullName = null)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var jwt = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
        var email = $"{role.ToLowerInvariant()}.{Guid.NewGuid():N}@integration.medipass.test";

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName ?? $"{role} Integration Test",
            Role = role,
            IsActive = true,
            EmailConfirmed = true
        };

        var userResult = await userManager.CreateAsync(user, TestPassword);
        if (!userResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create '{email}': {string.Join("; ", userResult.Errors.Select(e => e.Description))}");
        }

        var roleAssignment = await userManager.AddToRoleAsync(user, role);
        if (!roleAssignment.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not add '{email}' to '{role}': {string.Join("; ", roleAssignment.Errors.Select(e => e.Description))}");
        }

        return new TestUser(user.Id, email, jwt.CreateAccessToken(user, new[] { role }));
    }

    /// <summary>Creates an HTTP client that sends the user's bearer token with every request.</summary>
    /// <param name="user">The user returned by <see cref="CreateUserAsync"/> or a token-only test user.</param>
    /// <returns>A client that is not followed through redirects.</returns>
    public HttpClient CreateAuthenticatedClient(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        return client;
    }

    /// <summary>Creates a context for assertions against this run's isolated database.</summary>
    /// <returns>A context that the caller disposes.</returns>
    public MediPassDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        return new MediPassDbContext(options);
    }

    /// <summary>
    /// The connection string of this run's isolated database. The server behind it is shared with the test database,
    /// so a migration test can create its own scratch catalog on the same instance.
    /// </summary>
    public string ConnectionString => _connectionString;


    /// <summary>
    /// Seeds a clinic, a doctor account and profile, a patient account, a slot for today and an appointment for them.
    /// With <paramref name="includePendingRequest"/>, it also creates the pending request and its live QR credential.
    /// With <paramref name="accessCode"/>, it also seeds the HMAC of that six-digit code for the request.
    /// </summary>
    /// <param name="appointmentStatus">The initial appointment status.</param>
    /// <param name="includePendingRequest">Whether to create the pending request and QR credential.</param>
    /// <param name="accessCode">An optional plaintext code to seed as its stored HMAC.</param>
    /// <param name="accessCodeCreatedAtUtc">When the code was generated; defaults to now.</param>
    /// <returns>The seeded graph.</returns>
    public async Task<VisitScenario> CreateVisitScenarioAsync(
        AppointmentStatus appointmentStatus = AppointmentStatus.CheckedIn,
        bool includePendingRequest = true,
        string? accessCode = null,
        DateTime? accessCodeCreatedAtUtc = null)
    {
        var doctorUser = await CreateUserAsync(DefaultRoles.Doctor);
        var patientUser = await CreateUserAsync(DefaultRoles.Patient);

        await using var db = CreateDbContext();

        var clinic = new Clinic
        {
            Name = $"Integration Clinic {Guid.NewGuid():N}",
            Address = "1 Integration Way"
        };
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();

        var doctor = new Doctor
        {
            UserId = doctorUser.Id,
            Specialty = "General Medicine",
            ClinicId = clinic.Id
        };
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();

        var slotStart = ClinicDateAtNineUtc(ClinicTimeZone.GetLocalDate(DateTime.UtcNow));
        var slot = new DoctorSlot
        {
            DoctorId = doctor.Id,
            StartTimeUtc = slotStart,
            EndTimeUtc = slotStart.AddMinutes(30),
            IsAvailable = false
        };
        db.DoctorSlots.Add(slot);
        await db.SaveChangesAsync();

        var appointment = new Appointment
        {
            PatientId = patientUser.Id,
            DoctorId = doctor.Id,
            DoctorSlotId = slot.Id,
            Status = appointmentStatus,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        AccessRequest? request = null;
        VisitAccessCredential? credential = null;
        VisitAccessCredential? codeCredential = null;
        if (includePendingRequest)
        {
            request = AccessRequest.Create(appointment, doctor, DateTime.UtcNow);
            db.AccessRequests.Add(request);
            await db.SaveChangesAsync();

            credential = VisitAccessCredential.Create(
                request.Id,
                VisitCredentialType.QrToken,
                QrTokenGenerator.Generate().Sha256Hash,
                DateTime.UtcNow);
            db.VisitAccessCredentials.Add(credential);
            await db.SaveChangesAsync();

            if (accessCode is not null)
            {
                codeCredential = VisitAccessCredential.Create(
                    request.Id,
                    VisitCredentialType.NumericCode,
                    HashAccessCode(accessCode),
                    accessCodeCreatedAtUtc ?? DateTime.UtcNow);
                db.VisitAccessCredentials.Add(codeCredential);
                await db.SaveChangesAsync();
            }
        }

        return new VisitScenario(
            doctorUser,
            patientUser,
            clinic,
            doctor,
            slot,
            appointment,
            request,
            credential,
            codeCredential);
    }

    /// <summary>
    /// Approves the appointment's pending request, creating one when the scenario seeded none, and seeds its access
    /// grant. The appointment and doctor are reloaded so the grant is created against the persisted scope (ADR-0011
    /// decision 8).
    /// </summary>
    /// <param name="appointmentId">The appointment whose grant is seeded.</param>
    /// <param name="grantedAtUtc">When the patient approved; defaults to now.</param>
    /// <returns>The seeded grant.</returns>
    public async Task<AccessGrant> CreateGrantAsync(
        int appointmentId,
        DateTime? grantedAtUtc = null)
    {
        await using var db = CreateDbContext();
        var appointment = await db.Appointments
            .SingleAsync(candidate => candidate.Id == appointmentId);

        var request = await db.AccessRequests
            .Where(candidate => candidate.AppointmentId == appointmentId)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (request is null)
        {
            var doctor = await db.Doctors
                .SingleAsync(candidate => candidate.Id == appointment.DoctorId);
            request = AccessRequest.Create(appointment, doctor, DateTime.UtcNow);
            db.AccessRequests.Add(request);
        }

        if (request.Status == AccessRequestStatus.Pending)
        {
            request.Approve();
        }

        var grant = AccessGrant.Create(request, appointment, grantedAtUtc ?? DateTime.UtcNow);
        db.AccessGrants.Add(grant);
        await db.SaveChangesAsync();

        return grant;
    }

    /// <summary>
    /// Seeds a clinic, two doctor accounts with profiles, a patient account, a free future slot, an appointment on
    /// its own slot, a second free slot for rescheduling and a slot owned by the second doctor.
    /// </summary>
    /// <param name="appointmentStatus">The status of the seeded appointment.</param>
    /// <param name="slotStartUtc">The first slot start; defaults to tomorrow at 09:00 UTC.</param>
    /// <param name="freeSlotAvailable">Whether the first free slot starts as available.</param>
    /// <returns>The seeded booking graph.</returns>
    public async Task<BookingScenario> CreateBookingScenarioAsync(
        AppointmentStatus appointmentStatus = AppointmentStatus.Booked,
        DateTime? slotStartUtc = null,
        bool freeSlotAvailable = true)
    {
        var doctorUser = await CreateUserAsync(DefaultRoles.Doctor);
        var patientUser = await CreateUserAsync(DefaultRoles.Patient);
        var otherDoctorUser = await CreateUserAsync(DefaultRoles.Doctor);

        await using var db = CreateDbContext();

        var clinic = new Clinic
        {
            Name = $"Booking Clinic {Guid.NewGuid():N}",
            Address = "1 Integration Way"
        };
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();

        var doctor = new Doctor
        {
            UserId = doctorUser.Id,
            Specialty = "General Medicine",
            ClinicId = clinic.Id
        };
        var otherDoctor = new Doctor
        {
            UserId = otherDoctorUser.Id,
            Specialty = "Dermatology",
            ClinicId = clinic.Id
        };
        db.Doctors.AddRange(doctor, otherDoctor);
        await db.SaveChangesAsync();

        var start = slotStartUtc ?? DateTime.UtcNow.Date.AddDays(1).AddHours(9);

        var freeSlot = NewSlot(doctor.Id, start, freeSlotAvailable);
        var appointmentSlot = NewSlot(doctor.Id, start.AddHours(1), isAvailable: false);
        var targetSlot = NewSlot(doctor.Id, start.AddHours(2), isAvailable: true);
        var otherDoctorSlot = NewSlot(otherDoctor.Id, start, isAvailable: true);

        db.DoctorSlots.AddRange(freeSlot, appointmentSlot, targetSlot, otherDoctorSlot);
        await db.SaveChangesAsync();

        var appointment = new Appointment
        {
            PatientId = patientUser.Id,
            DoctorId = doctor.Id,
            DoctorSlotId = appointmentSlot.Id,
            Status = appointmentStatus,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        return new BookingScenario(
            doctorUser,
            patientUser,
            clinic,
            doctor,
            freeSlot,
            appointmentSlot,
            appointment,
            targetSlot,
            otherDoctorUser,
            otherDoctor,
            otherDoctorSlot);
    }

    /// <summary>
    /// Seeds a clinic with a doctor and a staff member, a second clinic with its own staff member, a patient, and one
    /// appointment on a slot in the doctor's clinic. The slot defaults to 09:00 Cairo time today so the "today"
    /// check is deterministic around a UTC midnight boundary.
    /// </summary>
    /// <param name="appointmentStatus">The initial appointment status.</param>
    /// <param name="slotStartUtc">An optional explicit slot start, in UTC.</param>
    /// <returns>The seeded status-flow graph.</returns>
    public async Task<AppointmentStatusScenario> CreateAppointmentStatusScenarioAsync(
        AppointmentStatus appointmentStatus = AppointmentStatus.Booked,
        DateTime? slotStartUtc = null)
    {
        var doctorUser = await CreateUserAsync(DefaultRoles.Doctor);
        var patientUser = await CreateUserAsync(DefaultRoles.Patient);
        var staffUser = await CreateUserAsync(DefaultRoles.Staff);
        var otherClinicStaffUser = await CreateUserAsync(DefaultRoles.Staff);
        var otherClinicDoctorUser = await CreateUserAsync(DefaultRoles.Doctor);

        await using var db = CreateDbContext();

        var clinic = new Clinic
        {
            Name = $"Status Clinic {Guid.NewGuid():N}",
            Address = "1 Integration Way"
        };
        var otherClinic = new Clinic
        {
            Name = $"Other Status Clinic {Guid.NewGuid():N}",
            Address = "2 Integration Way"
        };
        db.Clinics.AddRange(clinic, otherClinic);
        await db.SaveChangesAsync();

        var doctor = new Doctor
        {
            UserId = doctorUser.Id,
            Specialty = "General Medicine",
            ClinicId = clinic.Id
        };
        var otherClinicDoctor = new Doctor
        {
            UserId = otherClinicDoctorUser.Id,
            Specialty = "Dermatology",
            ClinicId = otherClinic.Id
        };
        db.Doctors.AddRange(doctor, otherClinicDoctor);
        db.ClinicStaff.AddRange(
            new ClinicStaff { UserId = staffUser.Id, ClinicId = clinic.Id },
            new ClinicStaff { UserId = otherClinicStaffUser.Id, ClinicId = otherClinic.Id });
        await db.SaveChangesAsync();

        var start = slotStartUtc ?? ClinicDateAtNineUtc(ClinicToday);
        var slot = NewSlot(doctor.Id, start, isAvailable: false);
        db.DoctorSlots.Add(slot);
        await db.SaveChangesAsync();

        var appointment = new Appointment
        {
            PatientId = patientUser.Id,
            DoctorId = doctor.Id,
            DoctorSlotId = slot.Id,
            Status = appointmentStatus,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        return new AppointmentStatusScenario(
            staffUser,
            otherClinicStaffUser,
            doctorUser,
            otherClinicDoctorUser,
            patientUser,
            clinic,
            otherClinic,
            doctor,
            otherClinicDoctor,
            slot,
            appointment);
    }

    /// <summary>
    /// Seeds one clinic with two doctors and a set of appointments that exercises the doctor schedule scope: two of
    /// the caller's appointments today, a cancelled one, another doctor's appointment and one tomorrow.
    /// </summary>
    /// <returns>The seeded schedule graph.</returns>
    public async Task<DoctorScheduleScenario> CreateDoctorScheduleScenarioAsync()
    {
        var doctorUser = await CreateUserAsync(DefaultRoles.Doctor);
        var otherDoctorUser = await CreateUserAsync(DefaultRoles.Doctor);
        var firstPatient = await CreateUserAsync(DefaultRoles.Patient, "Schedule Patient A");
        var secondPatient = await CreateUserAsync(DefaultRoles.Patient, "Schedule Patient B");
        var cancelledPatient = await CreateUserAsync(DefaultRoles.Patient, "Schedule Patient C");
        var otherDoctorPatient = await CreateUserAsync(DefaultRoles.Patient, "Schedule Patient D");
        var tomorrowPatient = await CreateUserAsync(DefaultRoles.Patient, "Schedule Patient E");

        await using var db = CreateDbContext();

        var clinic = new Clinic
        {
            Name = $"Schedule Clinic {Guid.NewGuid():N}",
            Address = "3 Integration Way"
        };
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();

        var doctor = new Doctor
        {
            UserId = doctorUser.Id,
            Specialty = "General Medicine",
            ClinicId = clinic.Id
        };
        var otherDoctor = new Doctor
        {
            UserId = otherDoctorUser.Id,
            Specialty = "Dermatology",
            ClinicId = clinic.Id
        };
        db.Doctors.AddRange(doctor, otherDoctor);
        await db.SaveChangesAsync();

        var today = ClinicToday;
        var todayStart = ClinicDateAtNineUtc(today);
        var tomorrowStart = ClinicDateAtNineUtc(today.AddDays(1));

        var bookedSlot = NewSlot(doctor.Id, todayStart, isAvailable: false);
        var checkedInSlot = NewSlot(doctor.Id, todayStart.AddMinutes(30), isAvailable: false);
        var cancelledSlot = NewSlot(doctor.Id, todayStart.AddHours(1), isAvailable: true);
        var otherDoctorSlot = NewSlot(otherDoctor.Id, todayStart.AddHours(2), isAvailable: false);
        var tomorrowSlot = NewSlot(doctor.Id, tomorrowStart, isAvailable: false);
        db.DoctorSlots.AddRange(bookedSlot, checkedInSlot, cancelledSlot, otherDoctorSlot, tomorrowSlot);
        await db.SaveChangesAsync();

        var bookedAppointment = NewAppointment(firstPatient.Id, doctor.Id, bookedSlot.Id, AppointmentStatus.Booked);
        var checkedInAppointment = NewAppointment(secondPatient.Id, doctor.Id, checkedInSlot.Id, AppointmentStatus.CheckedIn);
        var cancelledAppointment = NewAppointment(cancelledPatient.Id, doctor.Id, cancelledSlot.Id, AppointmentStatus.Cancelled);
        var otherDoctorAppointment = NewAppointment(otherDoctorPatient.Id, otherDoctor.Id, otherDoctorSlot.Id, AppointmentStatus.Booked);
        var tomorrowAppointment = NewAppointment(tomorrowPatient.Id, doctor.Id, tomorrowSlot.Id, AppointmentStatus.Booked);
        db.Appointments.AddRange(
            bookedAppointment,
            checkedInAppointment,
            cancelledAppointment,
            otherDoctorAppointment,
            tomorrowAppointment);
        await db.SaveChangesAsync();

        return new DoctorScheduleScenario(
            doctorUser,
            otherDoctorUser,
            clinic,
            doctor,
            otherDoctor,
            bookedAppointment,
            checkedInAppointment,
            cancelledAppointment,
            otherDoctorAppointment,
            tomorrowAppointment);
    }

    /// <summary>Disposes the host, drops the test database and stops the Testcontainers database.</summary>
    public async ValueTask CleanupAsync()
    {
        if (_cleanedUp)
        {
            return;
        }

        _cleanedUp = true;
        await base.DisposeAsync();
        DropDatabase();
        RestoreEnvironmentVariables();

        if (_sqlContainer is not null)
        {
            await _sqlContainer.DisposeAsync();
        }
    }

    /// <summary>Disposes the host and releases the test database (ADR-0006).</summary>
    public override async ValueTask DisposeAsync()
    {
        await CleanupAsync();
    }

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync() => await CleanupAsync();

    /// <summary>Runs the API in the test environment so the development demo seeder never executes.</summary>
    /// <param name="builder">The web host builder.</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The API's Development seeder must not run in tests; default roles are seeded by Program
        // for every environment, and the test factory creates only the users each test needs.
        builder.UseEnvironment("Testing");

        // The gated test controller lives in this assembly. It proves an HTTP request that passes
        // [RequireActiveGrant] is refused after the patient ends the grant (FR-ACS-09, NFR-PRI-02).
        builder.ConfigureTestServices(services =>
            services.AddControllers()
                .AddApplicationPart(typeof(MedicalRecordGateTestController).Assembly));
    }

    private static DoctorSlot NewSlot(int doctorId, DateTime startUtc, bool isAvailable) =>
        new()
        {
            DoctorId = doctorId,
            StartTimeUtc = startUtc,
            EndTimeUtc = startUtc.AddMinutes(30),
            IsAvailable = isAvailable
        };

    /// <summary>Returns the UTC instant of 09:00 on a clinic-local date, for deterministic "today" seeds.</summary>
    /// <param name="date">The clinic-local date in Africa/Cairo.</param>
    /// <returns>09:00 Cairo time as UTC.</returns>
    public DateTime ClinicDateAtNineUtc(DateOnly date) =>
        ClinicTimeZone.GetStartOfDayUtc(date).AddHours(9);

    private static Appointment NewAppointment(
        Guid patientId,
        int doctorId,
        int slotId,
        AppointmentStatus status) =>
        new()
        {
            PatientId = patientId,
            DoctorId = doctorId,
            DoctorSlotId = slotId,
            Status = status,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };

    private void ApplyMigrations()
    {
        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        using var db = new MediPassDbContext(options);
        db.Database.Migrate();
    }

    private void SetEnvironmentVariable(string name, string value)
    {
        _previousEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    private void RestoreEnvironmentVariables()
    {
        foreach (var (name, value) in _previousEnvironment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        _previousEnvironment.Clear();
    }

    private void DropDatabase()
    {
        SqlConnection.ClearAllPools();

        var masterConnectionString = new SqlConnectionStringBuilder(_connectionString)
        {
            InitialCatalog = "master"
        }.ConnectionString;

        using var connection = new SqlConnection(masterConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{_databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{_databaseName}];
            END
            """;
        command.ExecuteNonQuery();
    }
}

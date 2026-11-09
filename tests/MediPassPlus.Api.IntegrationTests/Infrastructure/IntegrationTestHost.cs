using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace MediPassPlus.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API pipeline once for the integration collection and owns its SQL Server database (ADR-0006).
/// Testcontainers SQL Server is the default; set <c>MEDIPASS_INTEGRATION_SQLSERVER</c> to use an existing server on a
/// machine without Docker.
/// </summary>
public sealed class IntegrationTestHost : IAsyncLifetime
{
    /// <summary>Environment variable that points the suite at an existing SQL Server instead of a container.</summary>
    public const string LocalServerVariable = "MEDIPASS_INTEGRATION_SQLSERVER";

    private const string TestPassword = "MediPass!Test1";
    private const string TestJwtIssuer = "MediPassPlus.IntegrationTests";
    private const string TestJwtAudience = "MediPassPlus.IntegrationTests.Client";
    private const string DatabasePrefix = "MediPassPlus_Integration_";
    private const string SqlImage = "mcr.microsoft.com/mssql/server:2022-latest";

    // Generated at runtime: the test signing key is never committed (NFR-SEC-08).
    private static readonly string TestJwtSecretKey =
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    private MsSqlContainer? _container;
    private string _serverConnectionString = string.Empty;
    private string _databaseName = string.Empty;
    private string _connectionString = string.Empty;
    private WebApplicationFactory<Program> _factory = null!;

    /// <summary>The unauthenticated client that also starts the host on first use.</summary>
    public HttpClient Client { get; private set; } = null!;

    /// <summary>The API service provider, used for Identity operations.</summary>
    public IServiceProvider Services => _factory.Services;

    /// <summary>The connection string of this run's isolated database.</summary>
    public string ConnectionString => _connectionString;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(LocalServerVariable);
        _serverConnectionString = string.IsNullOrWhiteSpace(configured)
            ? await StartContainerAsync()
            : configured;

        _databaseName = DatabasePrefix + Guid.NewGuid().ToString("N");
        _connectionString = await CreateDatabaseAsync(_serverConnectionString, _databaseName);
        await MigrateAsync(_connectionString);
        ApplyConfigurationEnvironment(_connectionString);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("IntegrationTests");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(ConfigurationOverrides(_connectionString)));
        });

        // Creating the first client starts the host, including the role seeder.
        Client = _factory.CreateClient(CreateClientOptions());
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await DropDatabaseAsync();

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Creates an HTTP client that talks to the in-memory server without a bearer token.</summary>
    public HttpClient CreateClient() => _factory.CreateClient(CreateClientOptions());

    /// <summary>Creates a user account in the requested role and returns its login credentials.</summary>
    public async Task<TestUser> CreateUserAsync(string role)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{role.ToLowerInvariant()}.{Guid.NewGuid():N}@example.test";

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = $"Integration {role}",
            Role = role,
            IsActive = true,
        };

        var created = await userManager.CreateAsync(user, TestPassword);
        Assert.True(created.Succeeded, Describe(created));

        var inRole = await userManager.AddToRoleAsync(user, role);
        Assert.True(inRole.Succeeded, Describe(inRole));

        return new TestUser(user.Id, email, TestPassword);
    }

    /// <summary>Signs the user in through <c>POST api/Auth/login</c> and returns an authenticated client.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(TestUser user)
    {
        using var response = await Client.PostAsJsonAsync(
            "/api/Auth/login",
            new { email = user.Email, password = user.Password });
        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("The login response was empty.");

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    /// <summary>
    /// Seeds a clinic, a doctor account and profile, a patient account, a slot for today and an appointment. With
    /// <paramref name="includePendingRequest"/>, it also creates the pending request and its live QR credential.
    /// </summary>
    public async Task<VisitScenario> CreateVisitScenarioAsync(
        AppointmentStatus appointmentStatus = AppointmentStatus.CheckedIn,
        bool includePendingRequest = true)
    {
        var doctorUser = await CreateUserAsync(DefaultRoles.Doctor);
        var patientUser = await CreateUserAsync(DefaultRoles.Patient);

        await using var db = CreateDbContext();

        var clinic = new Clinic
        {
            Name = $"Integration Clinic {Guid.NewGuid():N}",
            Address = "1 Integration Way",
        };
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();

        var doctor = new Doctor
        {
            UserId = doctorUser.Id,
            Specialty = "General Medicine",
            ClinicId = clinic.Id,
        };
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();

        var slotStart = DateTime.UtcNow.Date.AddHours(9);
        var slot = new DoctorSlot
        {
            DoctorId = doctor.Id,
            StartTimeUtc = slotStart,
            EndTimeUtc = slotStart.AddMinutes(30),
            IsAvailable = false,
        };
        db.DoctorSlots.Add(slot);
        await db.SaveChangesAsync();

        var appointment = new Appointment
        {
            PatientId = patientUser.Id,
            DoctorId = doctor.Id,
            DoctorSlotId = slot.Id,
            Status = appointmentStatus,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        AccessRequest? request = null;
        VisitAccessCredential? credential = null;
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
        }

        return new VisitScenario(
            doctorUser,
            patientUser,
            clinic,
            doctor,
            slot,
            appointment,
            request,
            credential);
    }

    /// <summary>Creates a context for assertions against the isolated test database.</summary>
    public MediPassDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        return new MediPassDbContext(options);
    }

    private async Task<string> StartContainerAsync()
    {
        _container = new MsSqlBuilder(SqlImage).Build();
        await _container.StartAsync();
        return _container.GetConnectionString();
    }

    private static async Task<string> CreateDatabaseAsync(string serverConnectionString, string databaseName)
    {
        var master = new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = "master",
        };

        await using (var connection = new SqlConnection(master.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{databaseName}]";
            await command.ExecuteNonQueryAsync();
        }

        return new SqlConnectionStringBuilder(serverConnectionString)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;
    }

    private static async Task MigrateAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using var db = new MediPassDbContext(options);
        await db.Database.MigrateAsync();
    }

    private async Task DropDatabaseAsync()
    {
        try
        {
            var master = new SqlConnectionStringBuilder(_serverConnectionString)
            {
                InitialCatalog = "master",
            };

            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync();

            await using var singleUser = connection.CreateCommand();
            singleUser.CommandText =
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE";
            await singleUser.ExecuteNonQueryAsync();

            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP DATABASE [{_databaseName}]";
            await drop.ExecuteNonQueryAsync();
        }
        catch (SqlException)
        {
            // Best-effort cleanup: a failed drop must not hide the test result. The container is disposed anyway.
        }
    }

    private static void ApplyConfigurationEnvironment(string connectionString)
    {
        // Environment variables are read by WebApplication.CreateBuilder before the API registers its services, so
        // they reach AddDbContext and AddJwtAuthentication even with minimal hosting.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "IntegrationTests");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", connectionString);
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestJwtIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestJwtAudience);
        Environment.SetEnvironmentVariable("Jwt__SecretKey", TestJwtSecretKey);
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "15");
        Environment.SetEnvironmentVariable("RateLimiting__QrApprove__PermitLimit", "5");
        Environment.SetEnvironmentVariable("RateLimiting__QrApprove__WindowMinutes", "15");
        Environment.SetEnvironmentVariable("Logging__LogLevel__Default", "Warning");
    }

    private static Dictionary<string, string?> ConfigurationOverrides(string connectionString) =>
        new()
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString,
            ["Jwt:Issuer"] = TestJwtIssuer,
            ["Jwt:Audience"] = TestJwtAudience,
            ["Jwt:SecretKey"] = TestJwtSecretKey,
            ["Jwt:AccessTokenMinutes"] = "15",
            ["RateLimiting:QrApprove:PermitLimit"] = "5",
            ["RateLimiting:QrApprove:WindowMinutes"] = "15",
            ["Logging:LogLevel:Default"] = "Warning",
        };

    private static WebApplicationFactoryClientOptions CreateClientOptions() =>
        new()
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        };

    private static string Describe(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(error => error.Description));

    private sealed record LoginResponse(string AccessToken);
}

/// <summary>An account created for one integration test; the password is a fixed test value.</summary>
public sealed record TestUser(Guid Id, string Email, string Password);

/// <summary>The seeded visit graph a decision test needs, including the pending request and QR credential.</summary>
public sealed record VisitScenario(
    TestUser DoctorUser,
    TestUser PatientUser,
    Clinic Clinic,
    Doctor Doctor,
    DoctorSlot Slot,
    Appointment Appointment,
    AccessRequest? Request,
    VisitAccessCredential? Credential);

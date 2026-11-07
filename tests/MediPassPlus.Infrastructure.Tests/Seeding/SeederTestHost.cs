using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Infrastructure.Tests.Seeding;

/// <summary>
/// Builds the real seeder graph against a private in-memory SQLite database, so a test can run
/// <see cref="DatabaseSeeder"/> twice and compare what was stored.
/// </summary>
internal sealed class SeederTestHost : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;

    private SeederTestHost(SqliteConnection connection, ServiceProvider services, string password)
    {
        _connection = connection;
        _services = services;
        Password = password;
    }

    /// <summary>The password configured for the demo accounts in this host.</summary>
    public string Password { get; }

    /// <summary>The service provider the seeders resolve from.</summary>
    public IServiceProvider Services => _services;

    /// <summary>
    /// Creates an isolated host. Pass <see cref="string.Empty"/> to model a developer who did not configure the
    /// demo password; pass <c>null</c> to let the host generate a strong test password.
    /// </summary>
    /// <param name="password">The demo password to configure, or <c>null</c> to generate one.</param>
    /// <returns>A ready host with an empty, created schema.</returns>
    public static async Task<SeederTestHost> CreateAsync(string? password = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var demoPassword = password ?? $"Demo#{Guid.NewGuid():N}aA1";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<MediPassDbContext>(options => options.UseSqlite(connection));

        // The same policy the API uses (Infrastructure/Extensions/IdentityServiceExtensions).
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<MediPassDbContext>();

        services.Configure<DemoSeedOptions>(options => options.DemoPassword = demoPassword);
        services.AddScoped<ISeeder, DemoDataSeeder>();
        services.AddScoped<DatabaseSeeder>();

        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediPassDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        return new SeederTestHost(connection, provider, demoPassword);
    }

    /// <summary>Runs every registered seeder in the production order.</summary>
    public async Task RunSeedersAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().RunAsync();
    }

    /// <summary>Counts the rows the seeder is expected to own.</summary>
    /// <returns>The current row counts.</returns>
    public async Task<SeederCounts> CountAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediPassDbContext>();

        return new SeederCounts(
            Clinics: await db.Clinics.CountAsync(),
            Doctors: await db.Doctors.CountAsync(),
            Slots: await db.DoctorSlots.CountAsync(),
            Users: await db.Users.CountAsync(),
            Staff: await db.ClinicStaff.CountAsync(),
            Admins: await db.Admins.CountAsync());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>Row counts owned by the demo seeder.</summary>
internal sealed record SeederCounts(int Clinics, int Doctors, int Slots, int Users, int Staff, int Admins);

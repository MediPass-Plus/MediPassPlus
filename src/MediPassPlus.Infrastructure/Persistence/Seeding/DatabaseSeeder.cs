using Microsoft.Extensions.Logging;

namespace MediPassPlus.Infrastructure.Persistence.Seeding;

public class DatabaseSeeder
{
    private readonly MediPassDbContext _db;
    private readonly IEnumerable<ISeeder> _seeders;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        MediPassDbContext db,
        IEnumerable<ISeeder> seeders,
        ILogger<DatabaseSeeder> logger)
    {
        _db = db;
        _seeders = seeders;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        foreach (var seeder in _seeders.OrderBy(s => s.Order))
        {
            _logger.LogInformation("Running seeder {Seeder}", seeder.GetType().Name);
            await seeder.SeedAsync(_db, ct);
        }
    }
}

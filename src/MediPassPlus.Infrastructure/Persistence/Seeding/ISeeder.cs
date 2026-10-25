namespace MediPassPlus.Infrastructure.Persistence.Seeding;

public interface ISeeder
{
    // Lower number runs first (for example, accounts before bookings).
    int Order { get; }

    Task SeedAsync(MediPassDbContext db, CancellationToken ct = default);
}

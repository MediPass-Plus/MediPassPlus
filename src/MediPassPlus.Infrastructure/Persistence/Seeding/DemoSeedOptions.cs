namespace MediPassPlus.Infrastructure.Persistence.Seeding;

/// <summary>
/// Local settings for the synthetic demo seed data. The demo password is deliberately not part of the repository:
/// each developer sets <c>Seed:DemoPassword</c> in user-secrets (NFR-SEC-08).
/// </summary>
public sealed class DemoSeedOptions
{
    /// <summary>Configuration section that holds the demo settings.</summary>
    public const string SectionName = "Seed";

    /// <summary>
    /// Password used for every demo account. When it is empty or missing, the demo seeder creates no accounts and
    /// writes a warning instead of falling back to a default password.
    /// </summary>
    public string? DemoPassword { get; set; }
}

using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

public class MediPassDbContext : DbContext
{
    public MediPassDbContext(DbContextOptions<MediPassDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Every module adds its own IEntityTypeConfiguration class.
        // They are picked up here automatically.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediPassDbContext).Assembly);
    }
}

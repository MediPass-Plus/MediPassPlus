using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Persistence;

public class MediPassDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public MediPassDbContext(DbContextOptions<MediPassDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // call the extension method to configure the Identity schema
        modelBuilder.ConfigureIdentitySchema();

        // Every module adds its own IEntityTypeConfiguration class.
        // They are picked up here automatically.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediPassDbContext).Assembly);
    }
}

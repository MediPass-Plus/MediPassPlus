using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MediPassPlus.Domain.Clinical;
namespace MediPassPlus.Infrastructure.Persistence;

public class MediPassDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<DoctorSlot> DoctorSlots => Set<DoctorSlot>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
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

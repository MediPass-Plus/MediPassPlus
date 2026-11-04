using Microsoft.EntityFrameworkCore;
using MediPassPlus.Domain.Clinical;
namespace MediPassPlus.Infrastructure.Persistence;

public class MediPassDbContext : DbContext
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

        // Every module adds its own IEntityTypeConfiguration class.
        // They are picked up here automatically.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediPassDbContext).Assembly);
    }
}

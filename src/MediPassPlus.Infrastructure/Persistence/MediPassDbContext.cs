using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Domain.StaffAdmin;
namespace MediPassPlus.Infrastructure.Persistence;

public class MediPassDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<DoctorSlot> DoctorSlots => Set<DoctorSlot>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AccessRequest> AccessRequests => Set<AccessRequest>();
    public DbSet<AccessGrant> AccessGrants => Set<AccessGrant>();
        public DbSet<ClinicStaff> ClinicStaff => Set<ClinicStaff>();
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<VisitAccessCredential> VisitAccessCredentials => Set<VisitAccessCredential>();
    /// <summary>The append-only audit entries written for medical and access-control events (ADR-0004).</summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
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

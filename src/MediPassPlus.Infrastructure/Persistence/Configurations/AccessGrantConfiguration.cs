using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.VisitAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPassPlus.Infrastructure.Persistence.Configurations;

public class AccessGrantConfiguration : IEntityTypeConfiguration<AccessGrant>
{
    public void Configure(EntityTypeBuilder<AccessGrant> builder)
    {
        builder.ToTable("AccessGrants", "access");

        builder.Property(g => g.EndReason)
               .HasConversion<string>()
               .HasMaxLength(30);

        builder.Property(g => g.GrantedAtUtc).IsRequired();
        builder.Property(g => g.ExpiresAtUtc).IsRequired();

        // One grant per request, and one grant per appointment: an ended grant is never reused (FR-ACS-11).
        builder.HasOne<AccessRequest>()
            .WithOne()
            .HasForeignKey<AccessGrant>(g => g.AccessRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Appointment>()
            .WithOne()
            .HasForeignKey<AccessGrant>(g => g.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(g => g.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(g => g.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

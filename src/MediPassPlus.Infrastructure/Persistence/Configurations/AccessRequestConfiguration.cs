using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.VisitAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPassPlus.Infrastructure.Persistence.Configurations;

public class AccessRequestConfiguration : IEntityTypeConfiguration<AccessRequest>
{
    public void Configure(EntityTypeBuilder<AccessRequest> builder)
    {
        builder.ToTable("AccessRequests", "access");

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.CreatedAtUtc).IsRequired();

        builder.HasIndex(r => r.AppointmentId);

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(r => r.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(r => r.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPassPlus.Infrastructure.Persistence.Configurations;

public class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        builder.ToTable("Doctors", "clinical");

        builder.Property(d => d.Specialty).IsRequired().HasMaxLength(100);

        // One doctor profile per user account
        builder.HasIndex(d => d.UserId).IsUnique();

        builder.HasOne(d => d.Clinic)
            .WithMany(c => c.Doctors)
            .HasForeignKey(d => d.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        // The doctor is linked to a user account
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

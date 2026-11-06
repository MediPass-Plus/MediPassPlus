using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.StaffAdmin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPassPlus.Infrastructure.Persistence.Configurations;

public class ClinicStaffConfiguration : IEntityTypeConfiguration<ClinicStaff>
{
    public void Configure(EntityTypeBuilder<ClinicStaff> builder)
    {
        builder.ToTable("ClinicStaff", "staffadmin");

        // One staff profile per user account
        builder.HasIndex(s => s.UserId).IsUnique();

        // The staff member belongs to one clinic
        builder.HasOne(s => s.Clinic)
            .WithMany()
            .HasForeignKey(s => s.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        // The staff member is linked to a user account
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
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

        // At most one pending request per appointment. The filtered unique index is the authoritative guard against
        // two concurrent start-visit calls creating two pending requests; the API maps the violation to 409.
        builder.HasIndex(r => r.AppointmentId, "IX_AccessRequests_AppointmentId_Pending")
            .IsUnique()
            .HasFilter("[Status] = 'Pending'");

        // Composite foreign key: the database rejects a request whose doctor or patient does not match the
        // appointment. The service still verifies the authenticated actor (FR-ACS-06, ADR-0011 decision 8).
        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(r => new { r.AppointmentId, r.DoctorId, r.PatientId })
            .HasPrincipalKey(a => new { a.Id, a.DoctorId, a.PatientId })
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

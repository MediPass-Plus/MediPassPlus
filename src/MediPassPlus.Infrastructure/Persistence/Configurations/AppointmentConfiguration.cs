using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPassPlus.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments", "clinical");

        // Principal key for the visit-access composite foreign keys: a request or grant can prove in the database
        // that its doctor and patient are the appointment's doctor and patient (FR-ACS-06, ADR-0011 decision 8).
        builder.HasAlternateKey(a => new { a.Id, a.DoctorId, a.PatientId });

        // Store the status as text (for example "Booked") so it is readable in the database
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        

        builder.HasOne(a => a.Doctor)
            .WithMany()
            .HasForeignKey(a => a.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.DoctorSlot)
            .WithMany()
            .HasForeignKey(a => new { a.DoctorSlotId, a.DoctorId })
            .HasPrincipalKey(s => new { s.Id, s.DoctorId })
            .OnDelete(DeleteBehavior.Restrict);

        // The patient is linked to a user account
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

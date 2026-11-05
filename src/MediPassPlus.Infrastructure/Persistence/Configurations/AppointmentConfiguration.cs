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

        // Store the status as text (for example "Booked") so it is readable in the database
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        

        builder.HasOne(a => a.Doctor)
            .WithMany()
            .HasForeignKey(a => a.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.DoctorSlot)
            .WithMany()
            .OnDelete(DeleteBehavior.Restrict);

        // The patient is linked to a user account
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

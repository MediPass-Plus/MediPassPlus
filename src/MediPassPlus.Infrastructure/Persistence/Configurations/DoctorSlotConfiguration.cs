using MediPassPlus.Domain.Clinical;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPassPlus.Infrastructure.Persistence.Configurations;

public class DoctorSlotConfiguration : IEntityTypeConfiguration<DoctorSlot>
{
    public void Configure(EntityTypeBuilder<DoctorSlot> builder)
    {
        builder.ToTable("DoctorSlots", "clinical");

        builder.HasAlternateKey(s => new { s.Id, s.DoctorId });

        // The availability flag is the fast listing signal. Making it a concurrency token means two bookings of
        // the same slot cannot both succeed: the loser's update matches no row (FR-BKG-03, NFR-REL-03).
        builder.Property(s => s.IsAvailable).IsConcurrencyToken();

        // The doctor's daily schedule reads one doctor's slots for a clinic-local day; this index keeps that range
        // scan narrow (FR-BKG-07).
        builder.HasIndex(s => new { s.DoctorId, s.StartTimeUtc }, "IX_DoctorSlots_DoctorId_StartTimeUtc");

        builder.HasOne(s => s.Doctor)
            .WithMany(d => d.Slots)
            .HasForeignKey(s => s.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

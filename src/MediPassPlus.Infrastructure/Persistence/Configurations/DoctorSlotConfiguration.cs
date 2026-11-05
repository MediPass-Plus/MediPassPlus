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
        builder.HasOne(s => s.Doctor)
            .WithMany(d => d.Slots)
            .HasForeignKey(s => s.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

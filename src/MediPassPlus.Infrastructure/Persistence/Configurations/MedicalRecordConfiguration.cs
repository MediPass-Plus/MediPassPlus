using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPassPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="MedicalRecord"/> to the <c>records.MedicalRecords</c> table: one unique record per patient and a
/// restrict foreign key, so no delete path can leave clinical rows behind (FR-REC-01, FR-REC-09, ADR-0005).
/// </summary>
public class MedicalRecordConfiguration : IEntityTypeConfiguration<MedicalRecord>
{
    /// <summary>Configures the table, the unique patient index and the restrict foreign key.</summary>
    /// <param name="builder">The entity type builder for <see cref="MedicalRecord"/>.</param>
    public void Configure(EntityTypeBuilder<MedicalRecord> builder)
    {
        builder.ToTable("MedicalRecords", "records");

        builder.Property(r => r.CreatedAtUtc).IsRequired();

        // One permanent record per patient. The unique index is the authoritative guard when two registrations race
        // (FR-REC-01).
        builder.HasIndex(r => r.PatientId).IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

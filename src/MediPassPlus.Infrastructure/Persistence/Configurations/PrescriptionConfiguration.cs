using MediPassPlus.Domain.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPassPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Prescription"/> to the <c>records.Prescriptions</c> table: the diagnosis foreign key is required,
/// deletes are restricted and the optional self reference records a correction inside the same diagnosis
/// (FR-REC-04, FR-REC-09, ADR-0005).
/// </summary>
public class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
{
    /// <summary>Configures the table, the encrypted text columns, the correction index and the restrict foreign keys.</summary>
    /// <param name="builder">The entity type builder for <see cref="Prescription"/>.</param>
    public void Configure(EntityTypeBuilder<Prescription> builder)
    {
        builder.ToTable("Prescriptions", "records");

        // Ciphertext columns: no maximum length, because the encrypted payload is longer than the plaintext
        // (ADR-0005, ADR-0007 decision 5).
        builder.Property(p => p.MedicineName).IsRequired();
        builder.Property(p => p.Dosage).IsRequired();

        builder.HasOne<Diagnosis>()
            .WithMany()
            .HasForeignKey(p => p.DiagnosisId)
            .OnDelete(DeleteBehavior.Restrict);

        // A corrected medicine or dose is a new row that supersedes the old one, never an in-place update.
        // The unique filtered index stops two rows from superseding the same prescription (ADR-0005).
        builder.HasOne<Prescription>()
            .WithMany()
            .HasForeignKey(p => p.SupersedesPrescriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.SupersedesPrescriptionId, "IX_Prescriptions_SupersedesPrescriptionId")
            .IsUnique()
            .HasFilter("[SupersedesPrescriptionId] IS NOT NULL");
    }
}

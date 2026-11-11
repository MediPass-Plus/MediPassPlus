using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPassPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Diagnosis"/> to the <c>records.Diagnoses</c> table: both foreign keys are required, every delete
/// path is restricted, the history index supports the newest-first page and the self reference carries the correction
/// chain (FR-REC-02, FR-REC-03, FR-REC-09, ADR-0005).
/// </summary>
public class DiagnosisConfiguration : IEntityTypeConfiguration<Diagnosis>
{
    /// <summary>Configures the table, the history and correction indexes and the restrict foreign keys.</summary>
    /// <param name="builder">The entity type builder for <see cref="Diagnosis"/>.</param>
    public void Configure(EntityTypeBuilder<Diagnosis> builder)
    {
        builder.ToTable("Diagnoses", "records");

        // Ciphertext column: no maximum length, because the encrypted payload is longer than the plaintext
        // (ADR-0005, ADR-0007 decision 5).
        builder.Property(d => d.Description).IsRequired();
        builder.Property(d => d.DiagnosedAtUtc).IsRequired();

        // Supports the patient history page, newest first (FR-REC-02).
        builder.HasIndex(d => new { d.MedicalRecordId, d.DiagnosedAtUtc });

        // A diagnosis always belongs to exactly one record and one visit; both columns are NOT NULL.
        builder.HasOne<MedicalRecord>()
            .WithMany()
            .HasForeignKey(d => d.MedicalRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(d => d.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // A correction points at the row it replaces and the old row stays visible. The filtered unique index stops
        // two rows from superseding the same diagnosis (FR-REC-09, ADR-0005).
        builder.HasOne<Diagnosis>()
            .WithMany()
            .HasForeignKey(d => d.SupersedesDiagnosisId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.SupersedesDiagnosisId, "IX_Diagnoses_SupersedesDiagnosisId")
            .IsUnique()
            .HasFilter("[SupersedesDiagnosisId] IS NOT NULL");
    }
}

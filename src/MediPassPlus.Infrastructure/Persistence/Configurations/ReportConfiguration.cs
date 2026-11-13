using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPassPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Report"/> to the <c>records.Reports</c> table: the record foreign key is required, the uploader is
/// stored explicitly and deletes are restricted (FR-REC-09, ADR-0005).
/// </summary>
public class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    /// <summary>Configures the table, the record timeline index and the restrict foreign keys.</summary>
    /// <param name="builder">The entity type builder for <see cref="Report"/>.</param>
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("Reports", "records");

        builder.Property(r => r.StoredFileName).IsRequired().HasMaxLength(Report.StoredFileNameMaxLength);
        builder.Property(r => r.ContentType).IsRequired().HasMaxLength(Report.ContentTypeMaxLength);
        builder.Property(r => r.SizeBytes).IsRequired();
        builder.Property(r => r.UploadedAtUtc).IsRequired();

        // Supports the record's report list, newest first.
        builder.HasIndex(r => new { r.MedicalRecordId, r.UploadedAtUtc });

        builder.HasOne<MedicalRecord>()
            .WithMany()
            .HasForeignKey(r => r.MedicalRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reports are record-level: the uploader is stored explicitly instead of being recovered from the audit log.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

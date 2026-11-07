using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.VisitAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPassPlus.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs", "audit");

        builder.Property(a => a.Action).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(a => a.EntityType).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(a => a.EntityId).IsRequired().HasMaxLength(100);
        builder.Property(a => a.OccurredAtUtc).IsRequired();
        builder.Property(a => a.IpAddress).IsRequired().HasMaxLength(45);

        // Supports "Who viewed my record", newest first (FR-PRV-03, NFR-PRI-05).
        builder.HasIndex(a => new { a.PatientId, a.OccurredAtUtc });

        // Audit history must outlive the rows it describes; every delete path is restricted (NFR-PRI-03).
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccessGrant>()
            .WithMany()
            .HasForeignKey(a => a.AccessGrantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

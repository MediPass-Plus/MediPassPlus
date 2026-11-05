using MediPassPlus.Domain.VisitAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPassPlus.Infrastructure.Persistence.Configurations;

public class VisitAccessCredentialConfiguration : IEntityTypeConfiguration<VisitAccessCredential>
{
    public void Configure(EntityTypeBuilder<VisitAccessCredential> builder)
    {
        builder.ToTable("VisitAccessCredentials", "access");

        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.SecretHash).IsRequired().HasMaxLength(128);
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.ExpiresAtUtc).IsRequired();
        builder.Property(c => c.FailedAttempts).HasDefaultValue(0);

        builder.HasIndex(c => c.SecretHash);

        // At most one live credential of each type per request; a replacement revokes the old one (US-22).
        builder.HasIndex(c => new { c.AccessRequestId, c.Type })
            .IsUnique()
            .HasFilter("[RevokedAtUtc] IS NULL AND [ConsumedAtUtc] IS NULL");

        builder.HasOne<AccessRequest>()
            .WithMany()
            .HasForeignKey(c => c.AccessRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

using MediPassPlus.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediPassPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// Stores refresh tokens as single-use hashed rows (FR-ACC-06, NFR-SEC-03). The plaintext token never reaches the
/// database, and <see cref="RefreshToken.RevokedAtUtc"/> is the concurrency token that lets only one rotation of
/// the same row win.
/// </summary>
public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        // Part of the identity area, next to the Identity tables, which all live in the "identity" schema.
        builder.ToTable("RefreshTokens", "identity");

        builder.HasKey(token => token.Id);

        // SHA-256 in upper-case hex is always 64 characters.
        builder.Property(token => token.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(token => token.CreatedAtUtc).IsRequired();
        builder.Property(token => token.ExpiresAtUtc).IsRequired();
        builder.Property(token => token.SessionStartedAtUtc).IsRequired();
        builder.Property(token => token.DeviceId).HasMaxLength(128);

        // Two rotations of the same row cannot both commit: the second update updates zero rows.
        builder.Property(token => token.RevokedAtUtc).IsConcurrencyToken();

        // The token is looked up by its hash on every refresh and logout.
        builder.HasIndex(token => token.TokenHash).IsUnique();

        // A detected replay revokes every live token of the account, and a session cap read scans by user.
        builder.HasIndex(token => token.UserId);

        // Expired and revoked rows are kept until a retention job removes them; this index is what lets that job scan
        // the table instead of reading it whole.
        builder.HasIndex(token => token.ExpiresAtUtc);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using MediPassPlus.Application.Auth;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Tests.Identity;

/// <summary>
/// Persistence guarantees of the refresh-token table (FR-ACC-06, NFR-SEC-03): only a hash is stored, the hash is
/// unique, and <c>RevokedAtUtc</c> is the concurrency token that lets only one rotation of a row win.
/// </summary>
public class RefreshTokenPersistenceTests
{
    private static readonly DateTime IssuedAt = new(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    private const string PlainToken = "the-plaintext-token-that-must-never-be-stored";

    [Fact]
    public async Task SaveChanges_StoresOnlyTheHashOfTheToken()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();

        var userId = await SeedUserAsync(db);
        var hash = RefreshTokenHasher.Hash(PlainToken);

        db.RefreshTokens.Add(RefreshToken.Issue(userId, hash, IssuedAt, Lifetime, SessionLifetime));
        await db.SaveChangesAsync();

        // The column holds the SHA-256 of the token, never the token itself. Querying the raw column keeps the
        // assertion independent of the entity mapping.
        var stored = await db.Database
            .SqlQueryRaw<string>("SELECT TokenHash AS Value FROM RefreshTokens")
            .SingleAsync();

        Assert.Equal(hash, stored);
        Assert.DoesNotContain(PlainToken, stored, StringComparison.Ordinal);
        Assert.Equal(64, stored.Length);
        Assert.Equal(stored.ToUpperInvariant(), stored);
    }

    [Fact]
    public async Task SaveChanges_WithATokenHashThatIsAlreadyStored_Throws()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();

        var userId = await SeedUserAsync(db);
        var hash = RefreshTokenHasher.Hash(PlainToken);

        db.RefreshTokens.Add(RefreshToken.Issue(userId, hash, IssuedAt, Lifetime, SessionLifetime));
        db.RefreshTokens.Add(RefreshToken.Issue(userId, hash, IssuedAt, Lifetime, SessionLifetime));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_WhenAnotherRequestRevokedTheSameRowFirst_ThrowsAConcurrencyConflict()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        await using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();

        var userId = await SeedUserAsync(db);
        var token = RefreshToken.Issue(
            userId,
            RefreshTokenHasher.Hash(PlainToken),
            IssuedAt,
            Lifetime,
            SessionLifetime);

        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();

        // Walked in through the raw table, like a rotation or a replay response that committed a moment ago: the
        // in-memory copy still believes RevokedAtUtc is null.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE RefreshTokens SET RevokedAtUtc = {0} WHERE Id = {1}",
            IssuedAt.AddMinutes(1),
            token.Id);

        token.Revoke(IssuedAt.AddMinutes(5));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }

    private static MediPassDbContext NewContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<MediPassDbContext>().UseSqlite(connection).Options);

    private static async Task<Guid> SeedUserAsync(MediPassDbContext db)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "refresh.owner@example.test",
            NormalizedUserName = "REFRESH.OWNER@EXAMPLE.TEST",
            Email = "refresh.owner@example.test",
            NormalizedEmail = "REFRESH.OWNER@EXAMPLE.TEST",
            FullName = "Refresh Owner",
            Role = "Patient",
            IsActive = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user.Id;
    }
}

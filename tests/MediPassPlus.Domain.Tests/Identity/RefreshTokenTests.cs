using MediPassPlus.Domain.Identity;

namespace MediPassPlus.Domain.Tests.Identity;

public class RefreshTokenTests
{
    private static readonly DateTime LoginAt = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    // The stored form is the SHA-256 of the token; the domain never sees or keeps the plaintext.
    private const string Hash = "9F2C0C1B7E4A5D3C8B6A0918F1E2D3C4B5A6978877665544332211FFEEDDCCBB";

    [Fact]
    public void Issue_OnLogin_ExpiresAfterTheTokenLifetime()
    {
        var token = RefreshToken.Issue(UserId, Hash, LoginAt, Lifetime, SessionLifetime);

        Assert.Equal(UserId, token.UserId);
        Assert.Equal(Hash, token.TokenHash);
        Assert.Equal(LoginAt, token.CreatedAtUtc);
        Assert.Equal(LoginAt, token.SessionStartedAtUtc);
        Assert.Equal(LoginAt.AddDays(7), token.ExpiresAtUtc);
        Assert.False(token.IsRevoked);
        Assert.Null(token.ReplacedByTokenId);
        Assert.True(token.IsActive(LoginAt));
    }

    [Fact]
    public void Issue_OnRotation_KeepsTheSessionStartOfTheFirstLogin()
    {
        var rotated = RefreshToken.Issue(
            UserId,
            Hash,
            LoginAt.AddDays(6),
            Lifetime,
            SessionLifetime,
            sessionStartedAtUtc: LoginAt);

        Assert.Equal(LoginAt, rotated.SessionStartedAtUtc);
        Assert.Equal(LoginAt.AddDays(13), rotated.ExpiresAtUtc);
    }

    [Fact]
    public void Issue_LateInTheSession_CapsTheExpiryAtTheSessionEnd()
    {
        var capped = RefreshToken.Issue(
            UserId,
            Hash,
            LoginAt.AddDays(28),
            Lifetime,
            SessionLifetime,
            sessionStartedAtUtc: LoginAt);

        // 28 + 7 days would pass the 30-day cap, so the cap wins.
        Assert.Equal(LoginAt.AddDays(30), capped.ExpiresAtUtc);
        Assert.True(capped.IsActive(LoginAt.AddDays(28)));
        Assert.False(capped.IsActive(LoginAt.AddDays(30)));
    }

    [Fact]
    public void Issue_AfterTheSessionEnd_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => RefreshToken.Issue(
            UserId,
            Hash,
            LoginAt.AddDays(31),
            Lifetime,
            SessionLifetime,
            sessionStartedAtUtc: LoginAt));
    }

    [Fact]
    public void Issue_WithASessionStartInTheFuture_Throws()
    {
        Assert.Throws<ArgumentException>(() => RefreshToken.Issue(
            UserId,
            Hash,
            LoginAt,
            Lifetime,
            SessionLifetime,
            sessionStartedAtUtc: LoginAt.AddMinutes(1)));
    }

    [Fact]
    public void Issue_WithoutAHash_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => RefreshToken.Issue(UserId, " ", LoginAt, Lifetime, SessionLifetime));
    }

    [Fact]
    public void IsActive_AtTheExpiryInstant_IsFalse()
    {
        var token = RefreshToken.Issue(UserId, Hash, LoginAt, Lifetime, SessionLifetime);

        Assert.True(token.IsActive(token.ExpiresAtUtc.AddTicks(-1)));
        Assert.False(token.IsActive(token.ExpiresAtUtc));
        Assert.False(token.IsActive(token.ExpiresAtUtc.AddDays(1)));
    }

    [Fact]
    public void Revoke_LinksTheReplacementAndStopsTheToken()
    {
        var token = RefreshToken.Issue(UserId, Hash, LoginAt, Lifetime, SessionLifetime);
        var replacementId = Guid.NewGuid();

        token.Revoke(LoginAt.AddHours(1), replacementId);

        Assert.True(token.IsRevoked);
        Assert.Equal(LoginAt.AddHours(1), token.RevokedAtUtc);
        Assert.Equal(replacementId, token.ReplacedByTokenId);
        Assert.False(token.IsActive(LoginAt.AddHours(2)));
    }

    [Fact]
    public void Revoke_WithoutAReplacement_LeavesTheLinkEmpty()
    {
        var token = RefreshToken.Issue(UserId, Hash, LoginAt, Lifetime, SessionLifetime);

        token.Revoke(LoginAt.AddHours(1));

        Assert.True(token.IsRevoked);
        Assert.Null(token.ReplacedByTokenId);
    }

    [Fact]
    public void Revoke_Twice_KeepsTheFirstRevocation()
    {
        var token = RefreshToken.Issue(UserId, Hash, LoginAt, Lifetime, SessionLifetime);
        var firstReplacement = Guid.NewGuid();

        token.Revoke(LoginAt.AddHours(1), firstReplacement);
        token.Revoke(LoginAt.AddHours(2), Guid.NewGuid());

        Assert.Equal(LoginAt.AddHours(1), token.RevokedAtUtc);
        Assert.Equal(firstReplacement, token.ReplacedByTokenId);
    }
}

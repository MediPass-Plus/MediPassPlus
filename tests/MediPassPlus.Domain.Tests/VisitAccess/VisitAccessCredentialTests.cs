using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Domain.Tests.VisitAccess;

public class VisitAccessCredentialTests
{
    private static readonly DateTime CreatedAt = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid RequestId = Guid.NewGuid();

    [Fact]
    public void Create_QrToken_ExpiresAfterTwoMinutes()
    {
        var credential = Create(VisitCredentialType.QrToken);

        Assert.NotEqual(Guid.Empty, credential.Id);
        Assert.Equal(RequestId, credential.AccessRequestId);
        Assert.Equal(VisitCredentialType.QrToken, credential.Type);
        Assert.Equal(CreatedAt, credential.CreatedAtUtc);
        Assert.Equal(CreatedAt.AddMinutes(2), credential.ExpiresAtUtc);
        Assert.True(credential.IsUsableAt(CreatedAt));
        Assert.True(credential.IsUsableAt(CreatedAt.AddMinutes(2).AddTicks(-1)));
        Assert.False(credential.IsUsableAt(CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Create_NumericCode_ExpiresAfterFiveMinutes()
    {
        var credential = Create(VisitCredentialType.NumericCode);

        Assert.Equal(CreatedAt.AddMinutes(5), credential.ExpiresAtUtc);
        Assert.True(credential.IsUsableAt(CreatedAt.AddMinutes(5).AddTicks(-1)));
        Assert.False(credential.IsUsableAt(CreatedAt.AddMinutes(5)));
    }

    [Fact]
    public void Consume_MarksTheCredentialUsed_AndCannotBeRepeated()
    {
        var credential = Create(VisitCredentialType.QrToken);

        credential.Consume(CreatedAt.AddSeconds(30));

        Assert.Equal(CreatedAt.AddSeconds(30), credential.ConsumedAtUtc);
        Assert.False(credential.IsUsableAt(CreatedAt.AddSeconds(31)));
        Assert.Throws<InvalidOperationException>(() => credential.Consume(CreatedAt.AddSeconds(31)));
    }

    [Fact]
    public void Consume_AfterExpiry_Throws()
    {
        var credential = Create(VisitCredentialType.NumericCode);

        Assert.Throws<InvalidOperationException>(() => credential.Consume(CreatedAt.AddMinutes(5)));
    }

    [Fact]
    public void Revoke_MakesTheCredentialUnusable_AndIsIdempotent()
    {
        var credential = Create(VisitCredentialType.NumericCode);

        credential.Revoke(CreatedAt.AddMinutes(1));
        credential.Revoke(CreatedAt.AddMinutes(2));

        Assert.Equal(CreatedAt.AddMinutes(1), credential.RevokedAtUtc);
        Assert.False(credential.IsUsableAt(CreatedAt.AddMinutes(1)));
    }

    [Fact]
    public void RegisterFailedAttempt_LocksTheCodeOnTheFifthWrongEntry()
    {
        var credential = Create(VisitCredentialType.NumericCode);

        for (var attempt = 1; attempt <= VisitAccessCredential.MaxFailedAttempts; attempt++)
        {
            credential.RegisterFailedAttempt(CreatedAt.AddSeconds(attempt));
        }

        Assert.Equal(VisitAccessCredential.MaxFailedAttempts, credential.FailedAttempts);
        Assert.Equal(CreatedAt.AddSeconds(5), credential.LockedAtUtc);
        Assert.False(credential.IsUsableAt(CreatedAt.AddSeconds(6)));
    }

    [Fact]
    public void RegisterFailedAttempt_OnTheQrToken_Throws()
    {
        var credential = Create(VisitCredentialType.QrToken);

        Assert.Throws<InvalidOperationException>(() => credential.RegisterFailedAttempt(CreatedAt.AddSeconds(1)));
    }

    [Fact]
    public void RegisterFailedAttempt_AfterLockout_Throws()
    {
        var credential = Create(VisitCredentialType.NumericCode);
        for (var attempt = 1; attempt <= VisitAccessCredential.MaxFailedAttempts; attempt++)
        {
            credential.RegisterFailedAttempt(CreatedAt.AddSeconds(attempt));
        }

        Assert.Throws<InvalidOperationException>(() => credential.RegisterFailedAttempt(CreatedAt.AddSeconds(6)));
    }

    [Fact]
    public void Create_WithUndefinedType_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VisitAccessCredential.Create(
                RequestId,
                (VisitCredentialType)99,
                "hash",
                CreatedAt));
    }

    [Fact]
    public void Create_WithAnEmptyHash_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => VisitAccessCredential.Create(
                RequestId,
                VisitCredentialType.QrToken,
                " ",
                CreatedAt));
    }

    [Fact]
    public void Create_WithAnEmptyRequestId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => VisitAccessCredential.Create(
                Guid.Empty,
                VisitCredentialType.QrToken,
                "hash",
                CreatedAt));
    }

    private static VisitAccessCredential Create(VisitCredentialType type) =>
        VisitAccessCredential.Create(RequestId, type, "hashed-secret", CreatedAt);
}

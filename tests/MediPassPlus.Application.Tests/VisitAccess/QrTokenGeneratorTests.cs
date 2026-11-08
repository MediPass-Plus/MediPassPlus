using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MediPassPlus.Application.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

public class QrTokenGeneratorTests
{
    [Fact]
    public void Generate_ReturnsA128BitUrlSafeToken()
    {
        var token = QrTokenGenerator.Generate();

        var bytes = Base64Url.DecodeFromChars(token.Value);

        Assert.Equal(16, bytes.Length);
        Assert.Equal(QrTokenGenerator.TokenSizeBytes, bytes.Length);
        Assert.Equal(22, token.Value.Length);
        Assert.DoesNotContain('+', token.Value);
        Assert.DoesNotContain('/', token.Value);
        Assert.DoesNotContain('=', token.Value);
    }

    [Fact]
    public void Hash_IsTheSha256OfTheUtf8TokenInUppercaseHex()
    {
        const string token = "a-test-qr-token";
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        var hash = QrTokenGenerator.Hash(token);

        Assert.Equal(expected, hash);
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9A-F]{64}$", hash);
    }

    [Fact]
    public void Generate_ReturnsADifferentTokenEachTime()
    {
        var first = QrTokenGenerator.Generate();
        var second = QrTokenGenerator.Generate();

        Assert.NotEqual(first.Value, second.Value);
        Assert.NotEqual(first.Sha256Hash, second.Sha256Hash);
    }

    [Fact]
    public void GeneratedToken_HashesToTheReturnedHash()
    {
        var token = QrTokenGenerator.Generate();

        Assert.Equal(QrTokenGenerator.Hash(token.Value), token.Sha256Hash);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Hash_WithAMissingToken_Throws(string token)
    {
        Assert.Throws<ArgumentException>(() => QrTokenGenerator.Hash(token));
    }
}

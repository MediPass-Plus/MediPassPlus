using System.Security.Cryptography;
using System.Text;
using MediPassPlus.Application.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

public class HmacAccessCodeHasherTests
{
    [Fact]
    public void Hash_MatchesTheRfc4231HmacSha256Vector()
    {
        // RFC 4231 test case 6: a key larger than the block size (131 bytes of 0xaa).
        var hasher = new HmacAccessCodeHasher(Enumerable.Repeat((byte)0xaa, 131).ToArray());

        var hash = hasher.Hash("Test Using Larger Than Block-Size Key - Hash Key First");

        Assert.Equal(
            "60E431591EE0B67F0D8A26AACBF5B77F8E0BC6213728C5140546040F0EE37F54",
            Convert.ToHexString(Convert.FromBase64String(hash)));
    }

    [Fact]
    public void Hash_IsDeterministicAndNotAPlainSha256()
    {
        var hasher = new HmacAccessCodeHasher(CreateKey());

        var first = hasher.Hash("123456");
        var second = hasher.Hash("123456");
        var plainSha256 = Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes("123456")));

        Assert.Equal(first, second);
        Assert.NotEqual(plainSha256, first);
    }

    [Fact]
    public void Verify_AcceptsTheCorrectCode()
    {
        var hasher = new HmacAccessCodeHasher(CreateKey());
        var hash = hasher.Hash("042915");

        Assert.True(hasher.Verify("042915", hash));
    }

    [Fact]
    public void Verify_RejectsASameLengthWrongCode()
    {
        var hasher = new HmacAccessCodeHasher(CreateKey());
        var hash = hasher.Hash("042915");

        Assert.False(hasher.Verify("042916", hash));
    }

    [Fact]
    public void Verify_RejectsAHashProducedWithAnotherKey()
    {
        var first = new HmacAccessCodeHasher(CreateKey());
        var second = new HmacAccessCodeHasher(Enumerable.Repeat((byte)0x42, 32).ToArray());
        var hash = first.Hash("123456");

        Assert.False(second.Verify("123456", hash));
    }

    [Theory]
    [InlineData("not-base64!!")]
    [InlineData("AAAA")]
    [InlineData("")]
    public void Verify_RejectsAMalformedStoredHashWithoutThrowing(string storedHash)
    {
        var hasher = new HmacAccessCodeHasher(CreateKey());

        Assert.False(hasher.Verify("123456", storedHash));
    }

    [Fact]
    public void Constructor_RejectsAKeyShorterThan256Bits()
    {
        Assert.Throws<ArgumentException>(() => new HmacAccessCodeHasher(new byte[31]));
    }

    [Fact]
    public void Hash_RejectsAnEmptyCode()
    {
        var hasher = new HmacAccessCodeHasher(CreateKey());

        Assert.Throws<ArgumentException>(() => hasher.Hash(" "));
    }

    private static byte[] CreateKey() => Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
}

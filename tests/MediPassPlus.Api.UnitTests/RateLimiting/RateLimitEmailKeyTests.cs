using System.Globalization;
using MediPassPlus.Api.RateLimiting;

namespace MediPassPlus.Api.UnitTests.RateLimiting;

/// <summary>
/// The shared email-key rules (ADR-0007 decision 2, FR-PRV-07): which policies read the body, how an address is
/// normalized, and how it is folded into the bounded shard the limiter caches.
/// </summary>
public class RateLimitEmailKeyTests
{
    [Theory]
    [InlineData(LoginRateLimitPolicy.Name)]
    [InlineData(PasswordResetRateLimitPolicy.RequestName)]
    [InlineData(PasswordResetRateLimitPolicy.SubmitName)]
    public void TheThreeEmailPolicies_ReadTheBody(string policyName)
    {
        Assert.True(RateLimitEmailKey.IsBodyKeyed(policyName));
    }

    [Theory]
    [InlineData(QrApproveRateLimitPolicy.Name)]
    [InlineData(AccessCodeRateLimitPolicy.GenerationName)]
    [InlineData(AccessCodeRateLimitPolicy.VerificationName)]
    [InlineData(ChangePasswordRateLimitPolicy.Name)]
    [InlineData("NotAPolicy")]
    [InlineData(null)]
    public void APolicyKeyedBySomethingOtherThanTheEmail_DoesNotReadTheBody(string? policyName)
    {
        Assert.False(RateLimitEmailKey.IsBodyKeyed(policyName));
    }

    [Fact]
    public void Bucket_IsStableForOneAddress()
    {
        Assert.Equal(
            RateLimitEmailKey.Bucket("patient@example.com"),
            RateLimitEmailKey.Bucket("patient@example.com"));
    }

    [Fact]
    public void Bucket_IsAShortHexShardLabelAndNeverCarriesTheAddress()
    {
        var bucket = RateLimitEmailKey.Bucket("patient@example.com");

        Assert.StartsWith("b", bucket, StringComparison.Ordinal);
        Assert.InRange(bucket.Length, 2, 5);
        Assert.All(bucket[1..], character => Assert.True(Uri.IsHexDigit(character)));

        var shard = int.Parse(bucket[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        Assert.InRange(shard, 0, RateLimitEmailKey.BucketCount - 1);
        Assert.DoesNotContain("patient", bucket, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bucket_FoldsManyAddressesIntoTheBoundedShardSet()
    {
        var shards = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < 500; index++)
        {
            var bucket = RateLimitEmailKey.Bucket($"patient{index}@example.com");
            var shard = int.Parse(bucket[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            Assert.InRange(shard, 0, RateLimitEmailKey.BucketCount - 1);
            shards.Add(bucket);
        }

        // 500 addresses over 16,384 shards: a handful of collisions is expected and harmless, but the addresses must
        // not each get their own key, or the shard would not bound anything.
        Assert.True(shards.Count < 500);
    }

    [Fact]
    public void Normalize_KeepsAnAddressAtTheRfcLimit()
    {
        var address = $"{new string('a', RateLimitEmailKey.MaximumEmailLength - "@example.com".Length)}@example.com";

        Assert.Equal(RateLimitEmailKey.MaximumEmailLength, address.Length);
        Assert.Equal(address, RateLimitEmailKey.Normalize(address));
    }

    [Fact]
    public void Normalize_RejectsAnAddressBeyondTheRfcLimit()
    {
        var address = $"{new string('a', RateLimitEmailKey.MaximumEmailLength)}@example.com";

        Assert.Null(RateLimitEmailKey.Normalize(address));
    }
}

using System.Security.Cryptography;
using MediPassPlus.Infrastructure.Privacy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace MediPassPlus.Infrastructure.Tests.Privacy;

/// <summary>
/// Round-trip, purpose-versioning and ciphertext-only proofs for the single diagnosis-text encryptor
/// (FR-PRV-04, NFR-SEC-05, ADR-0007 decision 5).
/// </summary>
public sealed class DiagnosisProtectorTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "MediPassPlus.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("Acute bronchitis, follow-up in two weeks.")]
    [InlineData("التهاب شعبي حاد - متابعة بعد أسبوعين")]
    [InlineData("Line one\nLine two\t- dosage 2x/day\r\n")]
    public void Protect_Then_Unprotect_RoundTrips(string plaintext)
    {
        var protector = CreateProtector(out _);

        var ciphertext = protector.Protect(plaintext);

        Assert.Equal(plaintext, protector.Unprotect(ciphertext));
    }

    [Fact]
    public void Protect_RoundTrips_The_Maximum_Plaintext_Length()
    {
        // ADR-0005 caps diagnosis plaintext at 4,000 characters; ciphertext growth must not corrupt it.
        var plaintext = new string('ا', 4_000);
        var protector = CreateProtector(out _);

        Assert.Equal(plaintext, protector.Unprotect(protector.Protect(plaintext)));
    }

    [Fact]
    public void Protect_Stores_Ciphertext_Only()
    {
        const string plaintext = "Type 2 diabetes mellitus with retinopathy";
        var protector = CreateProtector(out _);

        var ciphertext = protector.Protect(plaintext);

        Assert.NotEqual(plaintext, ciphertext);
        Assert.DoesNotContain(plaintext, ciphertext, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(ciphertext));

        // The stored value is the Data Protection payload itself, Base64Url text, so it fits the nvarchar(max) column.
        Assert.DoesNotContain('+', ciphertext);
        Assert.DoesNotContain('/', ciphertext);
        Assert.DoesNotContain('=', ciphertext);
        Assert.NotEmpty(Base64UrlTextEncoder.Decode(ciphertext));
    }

    [Fact]
    public void Two_Protectors_Over_The_Same_KeyRing_Agree()
    {
        var keysPath = Path.Combine(_root, "shared-ring");
        var first = new DiagnosisProtector(CreateProvider(keysPath));
        var second = new DiagnosisProtector(CreateProvider(keysPath));

        var ciphertext = first.Protect("Chronic migraine");

        Assert.Equal("Chronic migraine", second.Unprotect(ciphertext));
    }

    [Fact]
    public void KeyRing_Is_Written_Outside_The_Repository()
    {
        var keysPath = Path.Combine(_root, "keys");
        var protector = new DiagnosisProtector(CreateProvider(keysPath));

        protector.Protect("Iron-deficiency anaemia");

        var keyFiles = Directory.GetFiles(keysPath, "*.xml");
        Assert.NotEmpty(keyFiles);
        Assert.False(DataProtectionKeyPath.IsInsideRepository(keysPath, AppContext.BaseDirectory));
    }

    [Fact]
    public void Purpose_Is_Versioned()
    {
        Assert.Equal("MediPass.Diagnosis.v1", DiagnosisProtector.Purpose);
        Assert.Matches(@"^MediPass\.Diagnosis\.v\d+$", DiagnosisProtector.Purpose);
    }

    [Fact]
    public void Ciphertext_From_Another_Purpose_Is_Refused()
    {
        var provider = CreateProvider(Path.Combine(_root, "keys"));
        var ciphertext = new DiagnosisProtector(provider).Protect("Asthma");

        var otherPurpose = provider.CreateProtector("MediPass.Prescription.v1");

        Assert.Throws<CryptographicException>(() => otherPurpose.Unprotect(ciphertext));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Protect_Rejects_Empty_Plaintext(string plaintext)
    {
        var protector = CreateProtector(out _);

        Assert.Throws<ArgumentException>(() => protector.Protect(plaintext));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Unprotect_Rejects_Empty_Ciphertext(string ciphertext)
    {
        var protector = CreateProtector(out _);

        Assert.Throws<ArgumentException>(() => protector.Unprotect(ciphertext));
    }

    [Fact]
    public void Unprotect_Rejects_Invalid_Payload()
    {
        var protector = CreateProtector(out _);

        Assert.Throws<CryptographicException>(() => protector.Unprotect("not-a-data-protection-payload"));
    }

    private DiagnosisProtector CreateProtector(out string keysPath)
    {
        keysPath = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        return new DiagnosisProtector(CreateProvider(keysPath));
    }

    private static IDataProtectionProvider CreateProvider(string keysPath) =>
        DataProtectionProvider.Create(
            new DirectoryInfo(keysPath),
            builder => builder.SetApplicationName(DataProtectionServiceCollectionExtensions.ApplicationName));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

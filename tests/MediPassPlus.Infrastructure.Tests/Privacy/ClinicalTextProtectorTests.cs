using System.Security.Cryptography;
using MediPassPlus.Application.Privacy;
using MediPassPlus.Infrastructure.Privacy;
using Microsoft.AspNetCore.DataProtection;

namespace MediPassPlus.Infrastructure.Tests.Privacy;

/// <summary>
/// Purpose routing, round-trip and purpose isolation for the one clinical text contract the Record write path
/// consumes: diagnosis text must be byte-compatible with the MP-50 protector and prescription text must be unreadable
/// as diagnosis text (NFR-SEC-05, FR-PRV-04, ADR-0005, ADR-0007 decision 5).
/// </summary>
public sealed class ClinicalTextProtectorTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "MediPassPlus.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(ClinicalTextPurpose.Diagnosis, "Acute bronchitis, follow-up in two weeks.")]
    [InlineData(ClinicalTextPurpose.Prescription, "Amoxicillin 500 mg three times a day")]
    public void Protect_Then_Unprotect_RoundTrips(ClinicalTextPurpose purpose, string plaintext)
    {
        var protector = CreateProtector(out _);

        var ciphertext = protector.Protect(purpose, plaintext);

        Assert.NotEqual(plaintext, ciphertext);
        Assert.DoesNotContain(plaintext, ciphertext, StringComparison.Ordinal);
        Assert.Equal(plaintext, protector.Unprotect(purpose, ciphertext));
    }

    [Fact]
    public void Diagnosis_Payload_Is_Readable_By_The_Mp50_Protector()
    {
        var provider = CreateProvider(Path.Combine(_root, "diagnosis"));
        var protector = new ClinicalTextProtector(new DiagnosisProtector(provider), provider);

        var ciphertext = protector.Protect(ClinicalTextPurpose.Diagnosis, "Migraine without aura");

        // MP-50 owns the diagnosis purpose; the adapter must not introduce a second payload shape.
        Assert.Equal("Migraine without aura", new DiagnosisProtector(provider).Unprotect(ciphertext));
    }

    [Fact]
    public void Ciphertext_From_The_Other_Purpose_Is_Refused()
    {
        var provider = CreateProvider(Path.Combine(_root, "isolation"));
        var protector = new ClinicalTextProtector(new DiagnosisProtector(provider), provider);

        var prescriptionCiphertext = protector.Protect(ClinicalTextPurpose.Prescription, "Ibuprofen 400 mg");
        var diagnosisCiphertext = protector.Protect(ClinicalTextPurpose.Diagnosis, "Lower back pain");

        Assert.Throws<CryptographicException>(
            () => protector.Unprotect(ClinicalTextPurpose.Diagnosis, prescriptionCiphertext));
        Assert.Throws<CryptographicException>(
            () => protector.Unprotect(ClinicalTextPurpose.Prescription, diagnosisCiphertext));
    }

    [Fact]
    public void Prescription_Purpose_Is_Versioned()
    {
        Assert.Equal("MediPass.Prescription.v1", ClinicalTextPurposeNames.Prescription);
        Assert.Matches(@"^MediPass\.Prescription\.v\d+$", ClinicalTextPurposeNames.Prescription);
    }

    [Fact]
    public void An_Undefined_Purpose_Is_Rejected()
    {
        var protector = CreateProtector(out _);
        var undefined = (ClinicalTextPurpose)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => protector.Protect(undefined, "text"));
        Assert.Throws<ArgumentOutOfRangeException>(() => protector.Unprotect(undefined, "text"));
    }

    [Fact]
    public void Null_Arguments_Are_Rejected()
    {
        var protector = CreateProtector(out _);

        Assert.Throws<ArgumentNullException>(
            () => protector.Protect(ClinicalTextPurpose.Diagnosis, plaintext: null!));
        Assert.Throws<ArgumentNullException>(
            () => protector.Unprotect(ClinicalTextPurpose.Prescription, ciphertext: null!));
    }

    private ClinicalTextProtector CreateProtector(out string keysPath)
    {
        keysPath = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        var provider = CreateProvider(keysPath);
        return new ClinicalTextProtector(new DiagnosisProtector(provider), provider);
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

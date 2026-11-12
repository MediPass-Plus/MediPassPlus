using MediPassPlus.Domain.Records;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.Tests.Records;

public class RecordImmutabilityInterceptorTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);

    private static MediPassDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MediPassDbContext>()
            .UseInMemoryDatabase($"record-interceptor-{Guid.NewGuid():N}")
            .AddInterceptors(new RecordImmutabilityInterceptor())
            .Options;

        return new MediPassDbContext(options);
    }

    private static Diagnosis NewDiagnosis() => Diagnosis.Create(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        42,
        "ciphertext",
        Now);

    [Fact]
    public async Task Inserting_a_diagnosis_is_allowed()
    {
        await using var db = CreateContext();
        db.Diagnoses.Add(NewDiagnosis());

        await db.SaveChangesAsync();

        Assert.Equal(1, await db.Diagnoses.CountAsync());
    }

    [Fact]
    public async Task Updating_a_diagnosis_is_rejected()
    {
        await using var db = CreateContext();
        var diagnosis = NewDiagnosis();
        db.Diagnoses.Add(diagnosis);
        await db.SaveChangesAsync();

        db.Entry(diagnosis).Property(nameof(Diagnosis.Description)).CurrentValue = "another ciphertext";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("append-only", exception.Message);
    }

    [Fact]
    public async Task Deleting_a_prescription_is_rejected()
    {
        await using var db = CreateContext();
        var prescription = Prescription.Create(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "ciphertext",
            "ciphertext");
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync();

        db.Prescriptions.Remove(prescription);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("append-only", exception.Message);
    }

    [Fact]
    public void Updating_a_report_is_rejected_synchronously()
    {
        using var db = CreateContext();
        var report = Report.Create(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "stored-file.pdf",
            "application/pdf",
            1024,
            Now);
        db.Reports.Add(report);
        db.SaveChanges();

        db.Entry(report).Property(nameof(Report.ContentType)).CurrentValue = "image/png";

        var exception = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("append-only", exception.Message);
    }
}

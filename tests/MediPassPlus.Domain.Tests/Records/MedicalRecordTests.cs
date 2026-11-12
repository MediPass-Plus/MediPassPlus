using MediPassPlus.Domain.Records;

namespace MediPassPlus.Domain.Tests.Records;

public class MedicalRecordTests
{
    private static readonly Guid PatientId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Create_rejects_an_empty_patient_identifier()
    {
        Assert.Throws<ArgumentException>(() => MedicalRecord.Create(Guid.Empty, DateTime.UtcNow));
    }

    [Fact]
    public void Create_assigns_a_identifier_and_keeps_the_patient_and_time()
    {
        var createdAtUtc = new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);

        var record = MedicalRecord.Create(PatientId, createdAtUtc);

        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal(PatientId, record.PatientId);
        Assert.Equal(createdAtUtc, record.CreatedAtUtc);

        // Every new record uses a time-ordered identifier, so the key index stays clustered in insert order.
        Assert.Equal(7, record.Id.Version);
    }

    [Fact]
    public void Create_never_returns_the_same_identifier_twice()
    {
        var first = MedicalRecord.Create(PatientId, DateTime.UtcNow);
        var second = MedicalRecord.Create(PatientId, DateTime.UtcNow);

        Assert.NotEqual(first.Id, second.Id);
    }
}

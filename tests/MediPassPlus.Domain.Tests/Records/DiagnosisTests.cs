using MediPassPlus.Domain.Records;

namespace MediPassPlus.Domain.Tests.Records;

public class DiagnosisTests
{
    private static readonly Guid MedicalRecordId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const int AppointmentId = 42;
    private const string Ciphertext = "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA";

    [Fact]
    public void Create_rejects_an_empty_medical_record()
    {
        Assert.Throws<ArgumentException>(() =>
            Diagnosis.Create(Guid.Empty, AppointmentId, Ciphertext, DateTime.UtcNow));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_a_missing_appointment(int appointmentId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Diagnosis.Create(MedicalRecordId, appointmentId, Ciphertext, DateTime.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_missing_ciphertext(string description)
    {
        Assert.Throws<ArgumentException>(() =>
            Diagnosis.Create(MedicalRecordId, AppointmentId, description, DateTime.UtcNow));
    }

    [Fact]
    public void Create_rejects_an_empty_superseded_diagnosis()
    {
        Assert.Throws<ArgumentException>(() =>
            Diagnosis.Create(MedicalRecordId, AppointmentId, Ciphertext, DateTime.UtcNow, Guid.Empty));
    }

    [Fact]
    public void Create_keeps_every_value_it_was_given()
    {
        var diagnosedAtUtc = new DateTime(2026, 11, 12, 10, 30, 0, DateTimeKind.Utc);
        var supersedes = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var diagnosis = Diagnosis.Create(MedicalRecordId, AppointmentId, Ciphertext, diagnosedAtUtc, supersedes);

        Assert.NotEqual(Guid.Empty, diagnosis.Id);
        Assert.Equal(MedicalRecordId, diagnosis.MedicalRecordId);
        Assert.Equal(AppointmentId, diagnosis.AppointmentId);
        Assert.Equal(Ciphertext, diagnosis.Description);
        Assert.Equal(diagnosedAtUtc, diagnosis.DiagnosedAtUtc);
        Assert.Equal(supersedes, diagnosis.SupersedesDiagnosisId);
    }

    [Fact]
    public void A_first_diagnosis_does_not_supersede_anything()
    {
        var diagnosis = Diagnosis.Create(MedicalRecordId, AppointmentId, Ciphertext, DateTime.UtcNow);

        Assert.Null(diagnosis.SupersedesDiagnosisId);
    }
}

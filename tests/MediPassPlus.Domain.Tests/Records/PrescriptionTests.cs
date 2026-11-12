using MediPassPlus.Domain.Records;

namespace MediPassPlus.Domain.Tests.Records;

public class PrescriptionTests
{
    private static readonly Guid DiagnosisId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private const string MedicineCiphertext = "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA";
    private const string DosageCiphertext = "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAB";

    [Fact]
    public void Create_rejects_an_empty_diagnosis()
    {
        Assert.Throws<ArgumentException>(() =>
            Prescription.Create(Guid.Empty, MedicineCiphertext, DosageCiphertext));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_missing_medicine_name(string medicineName)
    {
        Assert.Throws<ArgumentException>(() =>
            Prescription.Create(DiagnosisId, medicineName, DosageCiphertext));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_missing_dosage(string dosage)
    {
        Assert.Throws<ArgumentException>(() =>
            Prescription.Create(DiagnosisId, MedicineCiphertext, dosage));
    }

    [Fact]
    public void Create_rejects_an_empty_superseded_prescription()
    {
        Assert.Throws<ArgumentException>(() =>
            Prescription.Create(DiagnosisId, MedicineCiphertext, DosageCiphertext, null, Guid.Empty));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_notes_are_stored_as_null(string? notes)
    {
        var prescription = Prescription.Create(DiagnosisId, MedicineCiphertext, DosageCiphertext, notes);

        Assert.Null(prescription.Notes);
    }

    [Fact]
    public void Create_keeps_every_value_it_was_given()
    {
        var supersedes = Guid.Parse("55555555-5555-5555-5555-555555555555");
        const string NotesCiphertext = "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAC";

        var prescription = Prescription.Create(
            DiagnosisId,
            MedicineCiphertext,
            DosageCiphertext,
            NotesCiphertext,
            supersedes);

        Assert.NotEqual(Guid.Empty, prescription.Id);
        Assert.Equal(DiagnosisId, prescription.DiagnosisId);
        Assert.Equal(MedicineCiphertext, prescription.MedicineName);
        Assert.Equal(DosageCiphertext, prescription.Dosage);
        Assert.Equal(NotesCiphertext, prescription.Notes);
        Assert.Equal(supersedes, prescription.SupersedesPrescriptionId);
    }
}

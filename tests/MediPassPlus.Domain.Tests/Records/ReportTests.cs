using MediPassPlus.Domain.Records;

namespace MediPassPlus.Domain.Tests.Records;

public class ReportTests
{
    private static readonly Guid MedicalRecordId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid UploadedByUserId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private const string StoredFileName = "0f8fad5b-d9cb-469f-a165-70867728950e.pdf";
    private const string ContentType = "application/pdf";
    private const long SizeBytes = 1024;

    private static Report Create() =>
        Report.Create(MedicalRecordId, UploadedByUserId, StoredFileName, ContentType, SizeBytes, DateTime.UtcNow);

    [Fact]
    public void Create_rejects_an_empty_medical_record()
    {
        Assert.Throws<ArgumentException>(() =>
            Report.Create(Guid.Empty, UploadedByUserId, StoredFileName, ContentType, SizeBytes, DateTime.UtcNow));
    }

    [Fact]
    public void Create_rejects_an_empty_uploader()
    {
        Assert.Throws<ArgumentException>(() =>
            Report.Create(MedicalRecordId, Guid.Empty, StoredFileName, ContentType, SizeBytes, DateTime.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_missing_stored_file_name(string storedFileName)
    {
        Assert.Throws<ArgumentException>(() =>
            Report.Create(MedicalRecordId, UploadedByUserId, storedFileName, ContentType, SizeBytes, DateTime.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_missing_content_type(string contentType)
    {
        Assert.Throws<ArgumentException>(() =>
            Report.Create(MedicalRecordId, UploadedByUserId, StoredFileName, contentType, SizeBytes, DateTime.UtcNow));
    }

    [Fact]
    public void Create_rejects_a_stored_file_name_longer_than_the_column()
    {
        var tooLong = new string('a', Report.StoredFileNameMaxLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Report.Create(MedicalRecordId, UploadedByUserId, tooLong, ContentType, SizeBytes, DateTime.UtcNow));
    }

    [Fact]
    public void Create_rejects_a_content_type_longer_than_the_column()
    {
        var tooLong = new string('a', Report.ContentTypeMaxLength + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Report.Create(MedicalRecordId, UploadedByUserId, StoredFileName, tooLong, SizeBytes, DateTime.UtcNow));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_a_file_without_content(long sizeBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Report.Create(MedicalRecordId, UploadedByUserId, StoredFileName, ContentType, sizeBytes, DateTime.UtcNow));
    }

    [Fact]
    public void Create_keeps_every_value_it_was_given()
    {
        var uploadedAtUtc = new DateTime(2026, 11, 12, 11, 0, 0, DateTimeKind.Utc);

        var report = Report.Create(
            MedicalRecordId,
            UploadedByUserId,
            StoredFileName,
            ContentType,
            SizeBytes,
            uploadedAtUtc);

        Assert.NotEqual(Guid.Empty, report.Id);
        Assert.Equal(MedicalRecordId, report.MedicalRecordId);
        Assert.Equal(UploadedByUserId, report.UploadedByUserId);
        Assert.Equal(StoredFileName, report.StoredFileName);
        Assert.Equal(ContentType, report.ContentType);
        Assert.Equal(SizeBytes, report.SizeBytes);
        Assert.Equal(uploadedAtUtc, report.UploadedAtUtc);
    }
}

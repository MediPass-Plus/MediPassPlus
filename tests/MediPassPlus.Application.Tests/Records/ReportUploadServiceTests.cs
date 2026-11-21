using System.Text;
using MediPassPlus.Application.Audit;
using MediPassPlus.Application.Privacy;
using MediPassPlus.Application.Records;
using MediPassPlus.Application.Tests.VisitAccess;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Records;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.Records;

/// <summary>
/// The report upload rules: the record owner is read from the stored row, a patient may only write their own record, a
/// doctor must hold an active grant for that patient, the file is validated against its leading bytes, and the report
/// row plus its audit entry commit together or the file is removed (FR-REC-05, FR-REC-06, FR-PRV-02, NFR-PRI-01,
/// NFR-SEC-09, ADR-0004, ADR-0015).
/// </summary>
public class ReportUploadServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherPatientId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid RecordId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private const int AppointmentId = 42;
    private const int DoctorId = 7;
    private const string IpAddress = "203.0.113.10";

    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\nreport\n%%EOF\n");
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01];

    [Fact]
    public async Task UploadAsync_WhenAPatientUploadsToTheirOwnRecord_StoresTheFileAndCommitsTheAuditRow()
    {
        var harness = new Harness();

        var result = await harness.Service.UploadAsync(
            RecordId,
            PatientId,
            ReportUploaderRole.Patient,
            new FakeUploadedFile(PdfBytes, ReportFileValidator.PdfContentType),
            IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReportUploadFailure.None, result.Failure);

        var report = Assert.Single(harness.Store.Reports);
        Assert.Equal(RecordId, report.MedicalRecordId);
        Assert.Equal(PatientId, report.UploadedByUserId);
        Assert.Equal(ReportFileValidator.PdfContentType, report.ContentType);
        Assert.Equal(PdfBytes.Length, report.SizeBytes);
        Assert.Equal(Now.UtcDateTime, report.UploadedAtUtc);
        Assert.Equal(report.Id, result.Report!.Id);

        Assert.Equal(1, harness.Store.SaveCount);
        Assert.Empty(harness.Storage.Deleted);
        Assert.Equal(PdfBytes, harness.Storage.LastContent);
        Assert.Equal(ReportFileValidator.PdfContentType, harness.Storage.LastContentType);

        var audit = Assert.Single(harness.Audit.AddedEntries);
        Assert.Equal(PatientId, audit.UserId);
        Assert.Equal(PatientId, audit.PatientId);
        Assert.Equal(AuditAction.ReportUploaded, audit.Action);
        Assert.Equal(AuditEntityType.Report, audit.EntityType);
        Assert.Equal(report.Id.ToString(), audit.EntityId);
        Assert.Equal(IpAddress, audit.IpAddress);
        Assert.Null(audit.AccessGrantId);
    }

    [Fact]
    public async Task UploadAsync_WhenAPatientUploadsToAnotherPatientsRecord_AnswersRecordNotFound()
    {
        var harness = new Harness();

        var result = await harness.Service.UploadAsync(
            RecordId,
            OtherPatientId,
            ReportUploaderRole.Patient,
            new FakeUploadedFile(PdfBytes, ReportFileValidator.PdfContentType),
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportUploadFailure.RecordNotFound, result.Failure);
        Assert.Empty(harness.Storage.Saved);
        Assert.Empty(harness.Store.Reports);
        Assert.Empty(harness.Audit.AddedEntries);
        Assert.Equal(0, harness.Store.SaveCount);
    }

    [Fact]
    public async Task UploadAsync_WhenTheRecordDoesNotExist_AnswersRecordNotFound()
    {
        var harness = new Harness();
        harness.Store.Scope = null;

        var result = await harness.Service.UploadAsync(
            RecordId,
            PatientId,
            ReportUploaderRole.Patient,
            new FakeUploadedFile(PdfBytes, ReportFileValidator.PdfContentType),
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportUploadFailure.RecordNotFound, result.Failure);
        Assert.True(harness.Store.WasFindCalled);
        Assert.Empty(harness.Storage.Saved);
    }

    [Fact]
    public async Task UploadAsync_WhenADoctorHoldsAnActiveGrant_StoresTheFileAndNamesTheGrantInTheAuditRow()
    {
        var harness = new Harness();
        var grant = CreateGrant();
        harness.Checker.Result = AccessGrantCheckResult.Allowed(grant);

        var result = await harness.Service.UploadAsync(
            RecordId,
            DoctorUserId,
            ReportUploaderRole.Doctor,
            new FakeUploadedFile(PngBytes, ReportFileValidator.PngContentType),
            IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Single(harness.Store.Reports);
        Assert.Equal(1, harness.Store.SaveCount);

        Assert.True(harness.Checker.WasCalled);
        Assert.Equal(DoctorUserId, harness.Checker.LastDoctorUserId);
        Assert.Equal(PatientId, harness.Checker.LastPatientUserId);

        var audit = Assert.Single(harness.Audit.AddedEntries);
        Assert.Equal(DoctorUserId, audit.UserId);
        Assert.Equal(PatientId, audit.PatientId);
        Assert.Equal(grant.Id, audit.AccessGrantId);
        Assert.Equal(AuditAction.ReportUploaded, audit.Action);
    }

    [Fact]
    public async Task UploadAsync_WhenADoctorHoldsNoActiveGrant_AnswersGrantNotActive()
    {
        var harness = new Harness();
        harness.Checker.Result = AccessGrantCheckResult.Denied(AccessGrantCheckFailure.GrantExpired);

        var result = await harness.Service.UploadAsync(
            RecordId,
            DoctorUserId,
            ReportUploaderRole.Doctor,
            new FakeUploadedFile(PdfBytes, ReportFileValidator.PdfContentType),
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportUploadFailure.GrantNotActive, result.Failure);
        Assert.True(harness.Checker.WasCalled);
        Assert.Empty(harness.Storage.Saved);
        Assert.Empty(harness.Audit.AddedEntries);
        Assert.Equal(0, harness.Store.SaveCount);
    }

    [Fact]
    public async Task UploadAsync_WhenTheGrantedPatientDiffersFromTheRecordOwner_AnswersGrantNotActive()
    {
        // Defence in depth: a grant for another patient authorizes nothing, even if the checker returned it.
        var harness = new Harness();
        harness.Checker.Result = AccessGrantCheckResult.Allowed(CreateGrant(OtherPatientId));

        var result = await harness.Service.UploadAsync(
            RecordId,
            DoctorUserId,
            ReportUploaderRole.Doctor,
            new FakeUploadedFile(PdfBytes, ReportFileValidator.PdfContentType),
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportUploadFailure.GrantNotActive, result.Failure);
        Assert.Empty(harness.Storage.Saved);
    }

    [Fact]
    public async Task UploadAsync_WhenTheFileIsLargerThanTenMegabytes_AnswersTooLargeBeforeStorage()
    {
        var harness = new Harness();
        var file = new FakeUploadedFile(PdfBytes, ReportFileValidator.PdfContentType)
        {
            Length = ReportFileValidator.MaxSizeBytes + 1
        };

        var result = await harness.Service.UploadAsync(
            RecordId,
            PatientId,
            ReportUploaderRole.Patient,
            file,
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportUploadFailure.TooLarge, result.Failure);
        Assert.Empty(harness.Storage.Saved);
        Assert.Empty(harness.Store.Reports);
    }

    [Fact]
    public async Task UploadAsync_WhenTheFileIsEmpty_AnswersEmptyFile()
    {
        var harness = new Harness();

        var result = await harness.Service.UploadAsync(
            RecordId,
            PatientId,
            ReportUploaderRole.Patient,
            new FakeUploadedFile([], ReportFileValidator.PdfContentType),
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportUploadFailure.EmptyFile, result.Failure);
        Assert.Empty(harness.Storage.Saved);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/pdfx")]
    [InlineData(null)]
    public async Task UploadAsync_WhenTheDeclaredTypeIsNotSupported_AnswersUnsupportedContentType(
        string? contentType)
    {
        var harness = new Harness();

        var result = await harness.Service.UploadAsync(
            RecordId,
            PatientId,
            ReportUploaderRole.Patient,
            new FakeUploadedFile(PdfBytes, contentType),
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportUploadFailure.UnsupportedContentType, result.Failure);
        Assert.Empty(harness.Storage.Saved);
    }

    [Fact]
    public async Task UploadAsync_WhenTheBytesDoNotMatchTheDeclaredType_AnswersContentMismatch()
    {
        var harness = new Harness();

        // A PNG renamed to PDF: the client's media type is allowed, the bytes are not.
        var result = await harness.Service.UploadAsync(
            RecordId,
            PatientId,
            ReportUploaderRole.Patient,
            new FakeUploadedFile(PngBytes, ReportFileValidator.PdfContentType),
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportUploadFailure.ContentMismatch, result.Failure);
        Assert.Empty(harness.Storage.Saved);
    }

    [Fact]
    public async Task UploadAsync_WhenTheStorageFindsMoreBytesThanDeclared_AnswersTooLarge()
    {
        var harness = new Harness();
        harness.Storage.ThrowSizeOnSave = ReportFileValidationFailure.TooLarge;

        var result = await harness.Service.UploadAsync(
            RecordId,
            PatientId,
            ReportUploaderRole.Patient,
            new FakeUploadedFile(PdfBytes, ReportFileValidator.PdfContentType),
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportUploadFailure.TooLarge, result.Failure);
        Assert.Empty(harness.Store.Reports);
        Assert.Empty(harness.Audit.AddedEntries);
    }

    [Fact]
    public async Task UploadAsync_WhenTheStorageFindsNoBytes_AnswersEmptyFile()
    {
        var harness = new Harness();
        harness.Storage.ThrowSizeOnSave = ReportFileValidationFailure.EmptyFile;

        var result = await harness.Service.UploadAsync(
            RecordId,
            PatientId,
            ReportUploaderRole.Patient,
            new FakeUploadedFile(PdfBytes, ReportFileValidator.PdfContentType),
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportUploadFailure.EmptyFile, result.Failure);
        Assert.Empty(harness.Store.Reports);
    }

    [Fact]
    public async Task UploadAsync_WhenTheStorageFailsUnexpectedly_PropagatesTheFailure()
    {
        // Only the dedicated size exception is a client refusal; an I/O or configuration failure must not be
        // mislabeled as 413 (FR-REC-05, NFR-SEC-09).
        var harness = new Harness();
        harness.Storage.ThrowUnexpectedOnSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.UploadAsync(
                RecordId,
                PatientId,
                ReportUploaderRole.Patient,
                new FakeUploadedFile(PdfBytes, ReportFileValidator.PdfContentType),
                IpAddress));

        Assert.Empty(harness.Store.Reports);
        Assert.Empty(harness.Audit.AddedEntries);
    }

    [Fact]
    public async Task UploadAsync_WhenTheCommitFails_DeletesTheStoredFileAndRethrows()
    {
        var harness = new Harness();
        harness.Store.ThrowOnSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.UploadAsync(
                RecordId,
                PatientId,
                ReportUploaderRole.Patient,
                new FakeUploadedFile(PdfBytes, ReportFileValidator.PdfContentType),
                IpAddress));

        var stored = Assert.Single(harness.Storage.Saved);
        Assert.Equal([stored.StoredFileName], harness.Storage.Deleted);
    }

    [Fact]
    public async Task UploadAsync_WhenTheCommitFailsAndCleanupFails_RethrowsTheCommitFailure()
    {
        var harness = new Harness();
        harness.Store.ThrowOnSave = true;
        harness.Storage.ThrowOnDelete = true;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.UploadAsync(
                RecordId,
                PatientId,
                ReportUploaderRole.Patient,
                new FakeUploadedFile(PdfBytes, ReportFileValidator.PdfContentType),
                IpAddress));

        // Cleanup is best effort: the original commit failure must be the one the caller sees.
        Assert.Equal("The commit failed.", exception.Message);
    }

    [Fact]
    public async Task UploadAsync_WhenTheAuditWriterThrows_DeletesTheStoredFileAndRethrows()
    {
        var harness = new Harness();
        harness.Audit.ThrowOnAdd = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.UploadAsync(
                RecordId,
                PatientId,
                ReportUploaderRole.Patient,
                new FakeUploadedFile(PdfBytes, ReportFileValidator.PdfContentType),
                IpAddress));

        var stored = Assert.Single(harness.Storage.Saved);
        Assert.Equal([stored.StoredFileName], harness.Storage.Deleted);
    }

    [Fact]
    public async Task UploadAsync_UsesARandomStoredNameWithTheValidatedExtension()
    {
        var harness = new Harness();

        await harness.Service.UploadAsync(
            RecordId,
            PatientId,
            ReportUploaderRole.Patient,
            new FakeUploadedFile(PngBytes, "image/png; charset=binary"),
            IpAddress);

        var stored = Assert.Single(harness.Storage.Saved);
        Assert.EndsWith(".png", stored.StoredFileName);
        Assert.Equal(PngBytes.Length, stored.SizeBytes);
    }

    [Theory]
    [InlineData("empty-record")]
    [InlineData("empty-caller")]
    public async Task UploadAsync_WithAnEmptyIdentifier_AnswersRecordNotFound(string scenario)
    {
        var harness = new Harness();

        var result = await harness.Service.UploadAsync(
            scenario == "empty-record" ? Guid.Empty : RecordId,
            scenario == "empty-caller" ? Guid.Empty : PatientId,
            ReportUploaderRole.Patient,
            new FakeUploadedFile(PdfBytes, ReportFileValidator.PdfContentType),
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportUploadFailure.RecordNotFound, result.Failure);
        Assert.False(harness.Store.WasFindCalled);
    }

    [Fact]
    public async Task UploadAsync_WithoutAFilePart_Throws()
    {
        var harness = new Harness();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Service.UploadAsync(
                RecordId,
                PatientId,
                ReportUploaderRole.Patient,
                null!,
                IpAddress));
    }

    [Fact]
    public async Task UploadAsync_WithoutAnIpAddress_Throws()
    {
        var harness = new Harness();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            harness.Service.UploadAsync(
                RecordId,
                PatientId,
                ReportUploaderRole.Patient,
                new FakeUploadedFile(PdfBytes, ReportFileValidator.PdfContentType),
                "  "));
    }

    [Fact]
    public async Task UploadAsync_WithAnUnknownRole_Throws()
    {
        var harness = new Harness();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            harness.Service.UploadAsync(
                RecordId,
                PatientId,
                (ReportUploaderRole)99,
                new FakeUploadedFile(PdfBytes, ReportFileValidator.PdfContentType),
                IpAddress));
    }

    private static AccessGrant CreateGrant(Guid? patientId = null)
    {
        var appointment = new Appointment
        {
            Id = AppointmentId,
            DoctorId = DoctorId,
            PatientId = patientId ?? PatientId,
            DoctorSlotId = 1
        };

        var doctor = new Doctor
        {
            Id = DoctorId,
            UserId = DoctorUserId,
            Specialty = "General",
            ClinicId = 1
        };

        var request = AccessRequest.Create(appointment, doctor, Now.UtcDateTime);
        request.Approve();

        var grant = AccessGrant.Create(request, appointment, Now.UtcDateTime);

        return grant;
    }

    private sealed class Harness
    {
        public FakeReportStore Store { get; } = new();

        public FakeAccessGrantChecker Checker { get; } = new();

        public FakeStorage Storage { get; } = new();

        public FakeAuditWriter Audit { get; } = new();

        public ReportUploadService Service { get; }

        public Harness()
        {
            Service = new ReportUploadService(
                Store,
                Checker,
                Storage,
                Audit,
                new FixedTimeProvider(Now));
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class FakeUploadedFile : IUploadedReportFile
    {
        private readonly byte[] _content;

        public FakeUploadedFile(byte[] content, string? contentType)
        {
            _content = content;
            ContentType = contentType;
            Length = content.Length;
        }

        public string? ContentType { get; }

        public long Length { get; init; }

        public Stream OpenReadStream() => new MemoryStream(_content, writable: false);
    }

    private sealed class FakeReportStore : IReportStore
    {
        public ReportRecordScope? Scope { get; set; } = new(RecordId, PatientId);

        public List<Report> Reports { get; } = [];

        public int SaveCount { get; private set; }

        public bool ThrowOnSave { get; set; }

        public bool WasFindCalled { get; private set; }

        public Task<ReportRecordScope?> FindRecordAsync(
            Guid recordId,
            CancellationToken cancellationToken = default)
        {
            WasFindCalled = true;

            return Task.FromResult(Scope);
        }

        public void Add(Report report) => Reports.Add(report);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;

            return ThrowOnSave
                ? Task.FromException(new InvalidOperationException("The commit failed."))
                : Task.CompletedTask;
        }
    }

    private sealed class FakeAccessGrantChecker : IAccessGrantChecker
    {
        public AccessGrantCheckResult Result { get; set; } =
            AccessGrantCheckResult.Denied(AccessGrantCheckFailure.GrantNotFound);

        public bool WasCalled { get; private set; }

        public Guid LastDoctorUserId { get; private set; }

        public Guid LastPatientUserId { get; private set; }

        public Task<AccessGrantCheckResult> RequireActiveGrantAsync(
            Guid doctorUserId,
            int appointmentId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The report upload resolves access from the record, not an appointment.");

        public Task<AccessGrantCheckResult> RequireActiveGrantForPatientAsync(
            Guid doctorUserId,
            Guid patientUserId,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastDoctorUserId = doctorUserId;
            LastPatientUserId = patientUserId;

            return Task.FromResult(Result);
        }
    }

    private sealed class FakeStorage : IReportStorage
    {
        public List<StoredReportFile> Saved { get; } = [];

        public List<string> Deleted { get; } = [];

        public byte[]? LastContent { get; private set; }

        public string? LastContentType { get; private set; }

        public bool ThrowUnexpectedOnSave { get; set; }

        public ReportFileValidationFailure? ThrowSizeOnSave { get; set; }

        public bool ThrowOnDelete { get; set; }

        public async Task<StoredReportFile> SaveAsync(
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            if (ThrowUnexpectedOnSave)
            {
                throw new InvalidOperationException("The storage is unavailable.");
            }

            if (ThrowSizeOnSave is { } failure)
            {
                throw new ReportFileSizeException(failure, "The stream disagreed with the declared size.");
            }

            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);

            LastContent = buffer.ToArray();
            LastContentType = contentType;

            var stored = new StoredReportFile(
                "0123456789abcdef0123456789abcdef" + ReportFileValidator.ExtensionFor(contentType),
                LastContent.Length);

            Saved.Add(stored);

            return stored;
        }

        public Task<bool> DeleteAsync(string storedFileName, CancellationToken cancellationToken = default)
        {
            if (ThrowOnDelete)
            {
                throw new IOException("The file is locked.");
            }

            Deleted.Add(storedFileName);

            return Task.FromResult(true);
        }
    }
}

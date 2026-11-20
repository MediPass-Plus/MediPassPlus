using System.IO;
using System.Text;
using MediPassPlus.Application.Audit;
using MediPassPlus.Application.Privacy;
using MediPassPlus.Application.Records;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Records;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.Records;

/// <summary>
/// The report download rules: the report and its owner are resolved from stored rows, a patient may read only their own
/// report, a doctor must hold an active grant for the owner, the file is served only after authorization, and every
/// successful download writes a <c>ReportDownloaded</c> audit row (FR-REC-08, FR-PRV-01, FR-PRV-02, FR-PRV-05,
/// NFR-PRI-01, ADR-0004, ADR-0015).
/// </summary>
public class ReportDownloadServiceTests
{
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherPatientId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ReportId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid RecordId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private const int AppointmentId = 42;
    private const int DoctorId = 7;
    private const string IpAddress = "203.0.113.10";
    private const string StoredFileName = "0123456789abcdef0123456789abcdef.pdf";

    private static readonly byte[] PdfBytes =
        Encoding.ASCII.GetBytes("%PDF-1.7\nsynthetic report\n%%EOF\n");

    [Fact]
    public async Task DownloadAsync_WhenThePatientOwnsTheReport_ReturnsTheStreamAndWritesAudit()
    {
        var harness = new Harness();

        using var result = await harness.Service.DownloadAsync(
            ReportId,
            PatientId,
            ReportUploaderRole.Patient,
            IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReportFileValidator.PdfContentType, result.ContentType);

        await using var stream = result.DetachContent();
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(PdfBytes, copy.ToArray());

        Assert.True(harness.Store.WasFindCalled);
        Assert.Equal(ReportId, harness.Store.LastReportId);
        Assert.True(harness.Storage.WasOpenCalled);
        Assert.Equal(StoredFileName, harness.Storage.LastOpenedName);
        Assert.False(harness.Checker.WasCalled);

        var audit = Assert.Single(harness.Audit.AddedEntries);
        Assert.Equal(PatientId, audit.UserId);
        Assert.Equal(PatientId, audit.PatientId);
        Assert.Equal(AuditAction.ReportDownloaded, audit.Action);
        Assert.Equal(AuditEntityType.Report, audit.EntityType);
        Assert.Equal(ReportId.ToString(), audit.EntityId);
        Assert.Equal(IpAddress, audit.IpAddress);
        Assert.Null(audit.AccessGrantId);
    }

    [Fact]
    public async Task DownloadAsync_WhenThePatientDoesNotOwnTheReport_ReturnsNotFoundWithoutOpeningTheFile()
    {
        var harness = new Harness();

        using var result = await harness.Service.DownloadAsync(
            ReportId,
            OtherPatientId,
            ReportUploaderRole.Patient,
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportDownloadFailure.NotFound, result.Failure);
        Assert.False(harness.Storage.WasOpenCalled);
        Assert.Empty(harness.Audit.AddedEntries);
    }

    [Fact]
    public async Task DownloadAsync_WhenTheReportDoesNotExist_ReturnsNotFound()
    {
        var harness = new Harness();
        harness.Store.Scope = null;

        using var result = await harness.Service.DownloadAsync(
            ReportId,
            PatientId,
            ReportUploaderRole.Patient,
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportDownloadFailure.NotFound, result.Failure);
        Assert.False(harness.Storage.WasOpenCalled);
        Assert.Empty(harness.Audit.AddedEntries);
    }

    [Fact]
    public async Task DownloadAsync_WhenADoctorHoldsAnActiveGrant_ReturnsTheStreamAndNamesTheGrantInAudit()
    {
        var harness = new Harness();
        var grant = CreateGrant();
        harness.Checker.Result = AccessGrantCheckResult.Allowed(grant);

        using var result = await harness.Service.DownloadAsync(
            ReportId,
            DoctorUserId,
            ReportUploaderRole.Doctor,
            IpAddress);

        Assert.True(result.IsSuccess);

        Assert.True(harness.Checker.WasCalled);
        Assert.Equal(DoctorUserId, harness.Checker.LastDoctorUserId);
        Assert.Equal(PatientId, harness.Checker.LastPatientUserId);

        var audit = Assert.Single(harness.Audit.AddedEntries);
        Assert.Equal(DoctorUserId, audit.UserId);
        Assert.Equal(PatientId, audit.PatientId);
        Assert.Equal(grant.Id, audit.AccessGrantId);
        Assert.Equal(AuditAction.ReportDownloaded, audit.Action);
    }

    [Fact]
    public async Task DownloadAsync_WhenADoctorHoldsNoActiveGrant_ReturnsNotFound()
    {
        var harness = new Harness();
        harness.Checker.Result = AccessGrantCheckResult.Denied(AccessGrantCheckFailure.GrantExpired);

        using var result = await harness.Service.DownloadAsync(
            ReportId,
            DoctorUserId,
            ReportUploaderRole.Doctor,
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportDownloadFailure.NotFound, result.Failure);
        Assert.True(harness.Checker.WasCalled);
        Assert.False(harness.Storage.WasOpenCalled);
        Assert.Empty(harness.Audit.AddedEntries);
    }

    [Fact]
    public async Task DownloadAsync_WhenTheGrantBelongsToAnotherPatient_ReturnsNotFound()
    {
        // Defence in depth: a grant for another patient authorizes nothing.
        var harness = new Harness();
        harness.Checker.Result = AccessGrantCheckResult.Allowed(CreateGrant(OtherPatientId));

        using var result = await harness.Service.DownloadAsync(
            ReportId,
            DoctorUserId,
            ReportUploaderRole.Doctor,
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportDownloadFailure.NotFound, result.Failure);
        Assert.False(harness.Storage.WasOpenCalled);
    }

    [Fact]
    public async Task DownloadAsync_WhenTheStoredFileIsMissing_ReturnsNotFoundWithoutAudit()
    {
        var harness = new Harness();
        harness.Storage.Missing = true;

        using var result = await harness.Service.DownloadAsync(
            ReportId,
            PatientId,
            ReportUploaderRole.Patient,
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportDownloadFailure.NotFound, result.Failure);
        Assert.True(harness.Storage.WasOpenCalled);
        Assert.Empty(harness.Audit.AddedEntries);
    }

    [Fact]
    public async Task DownloadAsync_WhenAuditWriteFails_DisposesTheStreamAndRethrows()
    {
        var harness = new Harness();
        harness.Audit.ThrowOnWrite = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.DownloadAsync(
                ReportId,
                PatientId,
                ReportUploaderRole.Patient,
                IpAddress));

        Assert.True(harness.Storage.WasOpenCalled);
        Assert.True(harness.Storage.LastStreamDisposed);
    }

    [Theory]
    [InlineData("empty-report")]
    [InlineData("empty-caller")]
    public async Task DownloadAsync_WithAnEmptyIdentifier_ReturnsNotFound(string scenario)
    {
        var harness = new Harness();

        using var result = await harness.Service.DownloadAsync(
            scenario == "empty-report" ? Guid.Empty : ReportId,
            scenario == "empty-caller" ? Guid.Empty : PatientId,
            ReportUploaderRole.Patient,
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReportDownloadFailure.NotFound, result.Failure);
        Assert.False(harness.Store.WasFindCalled);
    }

    [Fact]
    public async Task DownloadAsync_WithoutAnIpAddress_Throws()
    {
        var harness = new Harness();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            harness.Service.DownloadAsync(
                ReportId,
                PatientId,
                ReportUploaderRole.Patient,
                "  "));
    }

    [Fact]
    public async Task DownloadAsync_WithAnUnknownRole_Throws()
    {
        var harness = new Harness();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            harness.Service.DownloadAsync(
                ReportId,
                PatientId,
                (ReportUploaderRole)99,
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

        var request = AccessRequest.Create(appointment, doctor, new DateTime(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc));
        request.Approve();

        return AccessGrant.Create(request, appointment, new DateTime(2026, 11, 8, 9, 0, 0, DateTimeKind.Utc));
    }

    private sealed class Harness
    {
        public FakeReportStore Store { get; } = new();

        public FakeAccessGrantChecker Checker { get; } = new();

        public FakeStorage Storage { get; } = new();

        public FakeAuditWriter Audit { get; } = new();

        public ReportDownloadService Service { get; }

        public Harness()
        {
            Service = new ReportDownloadService(Store, Checker, Storage, Audit);
        }
    }

    private sealed class FakeReportStore : IReportStore
    {
        public ReportDownloadScope? Scope { get; set; } = new(
            ReportId,
            RecordId,
            PatientId,
            StoredFileName,
            ReportFileValidator.PdfContentType);

        public bool WasFindCalled { get; private set; }

        public Guid LastReportId { get; private set; }

        public Task<ReportRecordScope?> FindRecordAsync(
            Guid recordId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ReportRecordScope?>(null);

        public Task<ReportDownloadScope?> FindReportForDownloadAsync(
            Guid reportId,
            CancellationToken cancellationToken = default)
        {
            WasFindCalled = true;
            LastReportId = reportId;
            return Task.FromResult(Scope);
        }

        public void Add(Report report)
        {
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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
            throw new NotSupportedException("The report download resolves access from the report's owner.");

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
        public bool Missing { get; set; }

        public bool WasOpenCalled { get; private set; }

        public string? LastOpenedName { get; private set; }

        public bool LastStreamDisposed { get; private set; }

        public Task<StoredReportFile> SaveAsync(
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(string storedFileName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string storedFileName, CancellationToken cancellationToken = default)
        {
            WasOpenCalled = true;
            LastOpenedName = storedFileName;

            if (Missing)
            {
                return Task.FromResult<Stream?>(null);
            }

            return Task.FromResult<Stream?>(new TrackingMemoryStream(PdfBytes, () => LastStreamDisposed = true));
        }
    }

    private sealed class TrackingMemoryStream : MemoryStream
    {
        private readonly Action _onDispose;

        public TrackingMemoryStream(byte[] buffer, Action onDispose)
            : base(buffer, writable: false)
        {
            _onDispose = onDispose;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _onDispose();
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            _onDispose();
            return base.DisposeAsync();
        }
    }

    private sealed class FakeAuditWriter : IAuditWriter
    {
        public List<AuditEntry> AddedEntries { get; } = [];

        public bool ThrowOnWrite { get; set; }

        public AuditLog Add(AuditEntry entry)
        {
            AddedEntries.Add(entry);

            return AuditLog.Create(
                entry.UserId,
                entry.PatientId,
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.IpAddress,
                DateTimeOffset.UtcNow,
                entry.AccessGrantId);
        }

        public Task<AuditLog> WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            if (ThrowOnWrite)
            {
                throw new InvalidOperationException("The audit write failed.");
            }

            return Task.FromResult(Add(entry));
        }
    }
}

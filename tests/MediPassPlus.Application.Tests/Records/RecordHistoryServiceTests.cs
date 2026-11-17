using MediPassPlus.Application.Audit;
using MediPassPlus.Application.Privacy;
using MediPassPlus.Application.Records;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.Records;

/// <summary>
/// The read path rules: the record is resolved from a trusted identifier, nothing is decrypted before that
/// resolution succeeds, every read writes a <c>RecordRead</c> audit row with the right actor and grant, and both
/// pages are passed to the store untouched (FR-REC-02, FR-REC-07, FR-PRV-01, FR-PRV-02, NFR-PRF-04, ADR-0004,
/// ADR-0005).
/// </summary>
public class RecordHistoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MedicalRecordId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid FirstDiagnosisId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SecondDiagnosisId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid PrescriptionId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid ReportId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private const int AppointmentId = 42;
    private const int DoctorId = 7;
    private const string IpAddress = "203.0.113.10";

    [Fact]
    public async Task GetForPatientAsync_ReturnsTheDecryptedRecordAndAuditsTheSelfRead()
    {
        var harness = new Harness();
        harness.Store.Record = new MedicalRecordSummary(MedicalRecordId, Now.UtcDateTime.AddDays(-30));
        harness.Store.Diagnoses.AddRange(
            NewDiagnosis(FirstDiagnosisId, "cipher-first", Now.UtcDateTime),
            NewDiagnosis(SecondDiagnosisId, "cipher-second", Now.UtcDateTime.AddHours(-1)));
        harness.Store.Prescriptions.Add(new PrescriptionHistoryRow(
            PrescriptionId,
            FirstDiagnosisId,
            "cipher-medicine",
            "cipher-dosage",
            NotesCiphertext: null,
            SupersedesPrescriptionId: null));
        harness.Store.Reports.Add(new ReportMetadataRow(
            ReportId,
            "application/pdf",
            2048,
            Now.UtcDateTime.AddDays(-2),
            PatientId));

        var result = await harness.Service.GetForPatientAsync(
            PatientId,
            new RecordHistoryQuery(),
            IpAddress);

        Assert.True(result.IsSuccess);
        var history = result.History!;
        Assert.Equal(MedicalRecordId, history.MedicalRecordId);
        Assert.Equal(Now.UtcDateTime.AddDays(-30), history.RecordCreatedAtUtc);
        Assert.Equal(2, history.Diagnoses.TotalCount);
        Assert.Equal(2, history.Diagnoses.Items.Count);
        Assert.Equal("plain:cipher-first", history.Diagnoses.Items[0].Description);
        Assert.Equal("plain:cipher-second", history.Diagnoses.Items[1].Description);
        Assert.Equal(1, history.Reports.TotalCount);
        Assert.Equal(ReportId, Assert.Single(history.Reports.Items).Id);

        var prescription = Assert.Single(history.Diagnoses.Items[0].Prescriptions);
        Assert.Equal(PrescriptionId, prescription.Id);
        Assert.Equal("plain:cipher-medicine", prescription.MedicineName);
        Assert.Equal("plain:cipher-dosage", prescription.Dosage);
        Assert.Null(prescription.Notes);
        Assert.Empty(history.Diagnoses.Items[1].Prescriptions);

        var audit = Assert.Single(harness.Audit.Entries);
        Assert.Equal(AuditAction.RecordRead, audit.Action);
        Assert.Equal(AuditEntityType.MedicalRecord, audit.EntityType);
        Assert.Equal(MedicalRecordId.ToString(), audit.EntityId);
        Assert.Equal(PatientId, audit.UserId);
        Assert.Equal(PatientId, audit.PatientId);
        Assert.Null(audit.AccessGrantId);
        Assert.Equal(IpAddress, audit.IpAddress);
    }

    [Fact]
    public async Task GetForDoctorAsync_ResolvesThePatientFromTheGrantAndAuditsTheGrant()
    {
        var harness = new Harness();
        var grant = CreateGrant();
        harness.Store.Record = new MedicalRecordSummary(MedicalRecordId, Now.UtcDateTime);

        var result = await harness.Service.GetForDoctorAsync(
            AppointmentId,
            grant,
            DoctorUserId,
            new RecordHistoryQuery(),
            IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Equal(PatientId, harness.Store.LastPatientUserId);

        var audit = Assert.Single(harness.Audit.Entries);
        Assert.Equal(DoctorUserId, audit.UserId);
        Assert.Equal(PatientId, audit.PatientId);
        Assert.Equal(grant.Id, audit.AccessGrantId);
        Assert.Equal(AuditAction.RecordRead, audit.Action);
    }

    [Fact]
    public async Task GetForDoctorAsync_WhenTheGrantDescribesAnotherAppointment_ReturnsScopeMismatch()
    {
        var harness = new Harness();
        harness.Store.Record = new MedicalRecordSummary(MedicalRecordId, Now.UtcDateTime);

        var result = await harness.Service.GetForDoctorAsync(
            AppointmentId + 1,
            CreateGrant(),
            DoctorUserId,
            new RecordHistoryQuery(),
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(RecordHistoryFailure.ScopeMismatch, result.Failure);
        Assert.Equal(Guid.Empty, harness.Store.LastPatientUserId);
        Assert.Empty(harness.Audit.Entries);
        Assert.Empty(harness.Protector.UnprotectCalls);
    }

    [Fact]
    public async Task GetForPatientAsync_WhenTheRecordIsMissing_ReturnsRecordNotFoundWithoutAuditingOrDecrypting()
    {
        var harness = new Harness();
        harness.Store.Record = null;

        var result = await harness.Service.GetForPatientAsync(
            PatientId,
            new RecordHistoryQuery(),
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(RecordHistoryFailure.RecordNotFound, result.Failure);
        Assert.Empty(harness.Audit.Entries);
        Assert.Empty(harness.Protector.UnprotectCalls);
        Assert.False(harness.Store.PrescriptionsQueried);
    }

    [Fact]
    public async Task GetForPatientAsync_PassesBothPagesToTheStoreAndEchoesThemBack()
    {
        var harness = new Harness();
        harness.Store.Record = new MedicalRecordSummary(MedicalRecordId, Now.UtcDateTime);
        harness.Store.Diagnoses.AddRange(
            NewDiagnosis(FirstDiagnosisId, "cipher-one", Now.UtcDateTime),
            NewDiagnosis(SecondDiagnosisId, "cipher-two", Now.UtcDateTime.AddHours(-1)));

        var result = await harness.Service.GetForPatientAsync(
            PatientId,
            new RecordHistoryQuery
            {
                Page = 2,
                PageSize = 3,
                ReportPage = 2,
                ReportPageSize = 4
            },
            IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, harness.Store.LastDiagnosisSkip);
        Assert.Equal(3, harness.Store.LastDiagnosisTake);
        Assert.Equal(4, harness.Store.LastReportSkip);
        Assert.Equal(4, harness.Store.LastReportTake);
        Assert.Equal(2, result.History!.Diagnoses.Page);
        Assert.Equal(3, result.History.Diagnoses.PageSize);
        Assert.Equal(2, result.History.Reports.Page);
        Assert.Equal(4, result.History.Reports.PageSize);
    }

    [Fact]
    public async Task GetForPatientAsync_WhenTheDiagnosesPageIsEmpty_DoesNotQueryPrescriptions()
    {
        var harness = new Harness();
        harness.Store.Record = new MedicalRecordSummary(MedicalRecordId, Now.UtcDateTime);

        var result = await harness.Service.GetForPatientAsync(
            PatientId,
            new RecordHistoryQuery(),
            IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.History!.Diagnoses.Items);
        Assert.False(harness.Store.PrescriptionsQueried);
    }

    [Fact]
    public async Task GetForPatientAsync_KeepsTheCorrectionLinks()
    {
        var harness = new Harness();
        harness.Store.Record = new MedicalRecordSummary(MedicalRecordId, Now.UtcDateTime);
        harness.Store.Diagnoses.Add(NewDiagnosis(
            SecondDiagnosisId,
            "cipher-correction",
            Now.UtcDateTime,
            supersedesDiagnosisId: FirstDiagnosisId));

        var result = await harness.Service.GetForPatientAsync(
            PatientId,
            new RecordHistoryQuery(),
            IpAddress);

        var diagnosis = Assert.Single(result.History!.Diagnoses.Items);
        Assert.Equal(FirstDiagnosisId, diagnosis.SupersedesDiagnosisId);
    }

    [Fact]
    public async Task GetForPatientAsync_DecryptsOnlyTheRowsOnThePage()
    {
        var harness = new Harness();
        harness.Store.Record = new MedicalRecordSummary(MedicalRecordId, Now.UtcDateTime);
        for (var index = 0; index < 5; index++)
        {
            harness.Store.Diagnoses.Add(NewDiagnosis(
                Guid.NewGuid(),
                $"cipher-{index}",
                Now.UtcDateTime.AddMinutes(-index)));
        }

        var result = await harness.Service.GetForPatientAsync(
            PatientId,
            new RecordHistoryQuery { Page = 1, PageSize = 2 },
            IpAddress);

        Assert.Equal(2, result.History!.Diagnoses.Items.Count);
        Assert.Equal(5, result.History.Diagnoses.TotalCount);
        Assert.Equal(2, harness.Protector.UnprotectCalls.Count);
    }

    [Fact]
    public async Task GetForPatientAsync_SkipsTheRowsBeforeTheRequestedPage()
    {
        var harness = new Harness();
        harness.Store.Record = new MedicalRecordSummary(MedicalRecordId, Now.UtcDateTime);

        await harness.Service.GetForPatientAsync(
            PatientId,
            new RecordHistoryQuery { Page = 3, PageSize = 20 },
            IpAddress);

        Assert.Equal(40, harness.Store.LastDiagnosisSkip);
        Assert.Equal(20, harness.Store.LastDiagnosisTake);
    }

    private static DiagnosisHistoryRow NewDiagnosis(
        Guid id,
        string ciphertext,
        DateTime diagnosedAtUtc,
        Guid? supersedesDiagnosisId = null) =>
        new(id, AppointmentId, ciphertext, diagnosedAtUtc, supersedesDiagnosisId);

    private static AccessGrant CreateGrant(int appointmentId = AppointmentId)
    {
        var appointment = new Appointment
        {
            Id = appointmentId,
            DoctorId = DoctorId,
            PatientId = PatientId,
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

        return AccessGrant.Create(request, appointment, Now.UtcDateTime);
    }

    private sealed class Harness
    {
        public FakeRecordHistoryStore Store { get; } = new();

        public FakeClinicalTextProtector Protector { get; } = new();

        public FakeAuditWriter Audit { get; } = new();

        public RecordHistoryService Service { get; }

        public Harness()
        {
            Service = new RecordHistoryService(Store, Protector, Audit);
        }
    }

    private sealed class FakeRecordHistoryStore : IRecordHistoryStore
    {
        public MedicalRecordSummary? Record { get; set; }

        public List<DiagnosisHistoryRow> Diagnoses { get; } = [];

        public List<PrescriptionHistoryRow> Prescriptions { get; } = [];

        public List<ReportMetadataRow> Reports { get; } = [];

        public Guid LastPatientUserId { get; private set; }

        public int LastDiagnosisSkip { get; private set; }

        public int LastDiagnosisTake { get; private set; }

        public int LastReportSkip { get; private set; }

        public int LastReportTake { get; private set; }

        public bool PrescriptionsQueried { get; private set; }

        public Task<MedicalRecordSummary?> FindRecordAsync(
            Guid patientUserId,
            CancellationToken cancellationToken = default)
        {
            LastPatientUserId = patientUserId;
            return Task.FromResult(Record);
        }

        public Task<PagedRows<DiagnosisHistoryRow>> ListDiagnosesAsync(
            Guid medicalRecordId,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            LastDiagnosisSkip = skip;
            LastDiagnosisTake = take;

            var items = Diagnoses.Skip(skip).Take(take).ToList();
            return Task.FromResult(new PagedRows<DiagnosisHistoryRow>(items, Diagnoses.Count));
        }

        public Task<IReadOnlyList<PrescriptionHistoryRow>> ListPrescriptionsAsync(
            Guid medicalRecordId,
            IReadOnlyCollection<Guid> diagnosisIds,
            CancellationToken cancellationToken = default)
        {
            PrescriptionsQueried = true;

            IReadOnlyList<PrescriptionHistoryRow> items = Prescriptions
                .Where(prescription => diagnosisIds.Contains(prescription.DiagnosisId))
                .ToList();

            return Task.FromResult(items);
        }

        public Task<PagedRows<ReportMetadataRow>> ListReportsAsync(
            Guid medicalRecordId,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            LastReportSkip = skip;
            LastReportTake = take;

            var items = Reports.Skip(skip).Take(take).ToList();
            return Task.FromResult(new PagedRows<ReportMetadataRow>(items, Reports.Count));
        }
    }

    private sealed record UnprotectCall(ClinicalTextPurpose Purpose, string Ciphertext);

    private sealed class FakeClinicalTextProtector : IClinicalTextProtector
    {
        public List<UnprotectCall> UnprotectCalls { get; } = [];

        public string Protect(ClinicalTextPurpose purpose, string plaintext) =>
            throw new NotSupportedException("The read path never encrypts.");

        public string Unprotect(ClinicalTextPurpose purpose, string ciphertext)
        {
            UnprotectCalls.Add(new UnprotectCall(purpose, ciphertext));

            return $"plain:{ciphertext}";
        }
    }

    private sealed class FakeAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public AuditLog Add(AuditEntry entry) =>
            throw new NotSupportedException("A read commits its own audit row through WriteAsync.");

        public Task<AuditLog> WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);

            return Task.FromResult<AuditLog>(null!);
        }
    }
}

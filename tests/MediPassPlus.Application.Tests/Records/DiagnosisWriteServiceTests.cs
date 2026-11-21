using MediPassPlus.Application.Audit;
using MediPassPlus.Application.Privacy;
using MediPassPlus.Application.Records;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Records;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.Tests.Records;

/// <summary>
/// The doctor write path rules: the grant re-check, the visit status guard from ADR-0005, the diagnosis scope of a
/// prescription, the encryption purpose, and the audit row committed with the medical row (FR-REC-03, FR-REC-04,
/// FR-PRV-01, NFR-SEC-05).
/// </summary>
public class DiagnosisWriteServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid DoctorUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PatientId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherPatientId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid MedicalRecordId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid DiagnosisId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private const int AppointmentId = 42;
    private const int DoctorId = 7;
    private const string IpAddress = "203.0.113.10";

    [Fact]
    public async Task CreateDiagnosisAsync_WhenTheGrantIsActiveAndTheVisitIsInProgress_StoresTheEncryptedDiagnosis()
    {
        var harness = new Harness();
        var grant = CreateGrant();
        harness.Checker.Result = AccessGrantCheckResult.Allowed(grant);
        harness.Store.Context = new DiagnosisWriteContext(AppointmentStatus.InVisit, MedicalRecordId);

        var result = await harness.Service.CreateDiagnosisAsync(
            AppointmentId,
            DoctorUserId,
            new CreateDiagnosisRequest { Description = "Acute bronchitis" },
            IpAddress);

        Assert.True(result.IsSuccess);
        var diagnosis = Assert.Single(harness.Store.Diagnoses);
        Assert.Equal(MedicalRecordId, diagnosis.MedicalRecordId);
        Assert.Equal(AppointmentId, diagnosis.AppointmentId);
        Assert.Equal("cipher:Diagnosis:Acute bronchitis", diagnosis.Description);
        Assert.Equal(Now.UtcDateTime, diagnosis.DiagnosedAtUtc);
        Assert.Equal(1, harness.Store.SaveCount);

        var protectCall = Assert.Single(harness.Protector.Calls);
        Assert.Equal(ClinicalTextPurpose.Diagnosis, protectCall.Purpose);
        Assert.Equal("Acute bronchitis", protectCall.Plaintext);

        var audit = Assert.Single(harness.Audit.Entries);
        Assert.Equal(DoctorUserId, audit.UserId);
        Assert.Equal(PatientId, audit.PatientId);
        Assert.Equal(AuditAction.DiagnosisCreated, audit.Action);
        Assert.Equal(AuditEntityType.Diagnosis, audit.EntityType);
        Assert.Equal(diagnosis.Id.ToString(), audit.EntityId);
        Assert.Equal(IpAddress, audit.IpAddress);
        Assert.Equal(grant.Id, audit.AccessGrantId);

        Assert.Equal(diagnosis.Id, result.Diagnosis!.Id);
        Assert.Equal(AppointmentId, result.Diagnosis.AppointmentId);
        Assert.Equal(Now.UtcDateTime, result.Diagnosis.DiagnosedAtUtc);
    }

    [Fact]
    public async Task CreateDiagnosisAsync_WhenTheGrantIsRefused_ReturnsGrantNotActive()
    {
        var harness = new Harness();
        harness.Checker.Result = AccessGrantCheckResult.Denied(AccessGrantCheckFailure.GrantExpired);

        var result = await harness.Service.CreateDiagnosisAsync(
            AppointmentId,
            DoctorUserId,
            new CreateDiagnosisRequest { Description = "Acute bronchitis" },
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(DiagnosisWriteFailure.GrantNotActive, result.Failure);
        Assert.Empty(harness.Store.Diagnoses);
        Assert.Empty(harness.Audit.Entries);
        Assert.Equal(0, harness.Store.SaveCount);
    }

    [Fact]
    public async Task CreateDiagnosisAsync_WhenTheAppointmentIsMissing_ReturnsAppointmentNotFound()
    {
        var harness = new Harness();
        harness.Checker.Result = AccessGrantCheckResult.Allowed(CreateGrant());
        harness.Store.Context = null;

        var result = await harness.Service.CreateDiagnosisAsync(
            AppointmentId,
            DoctorUserId,
            new CreateDiagnosisRequest { Description = "Acute bronchitis" },
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(DiagnosisWriteFailure.AppointmentNotFound, result.Failure);
        Assert.Equal(0, harness.Store.SaveCount);
    }

    [Theory]
    [InlineData(AppointmentStatus.Booked)]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task CreateDiagnosisAsync_WhenTheVisitIsNotOpen_ReturnsAppointmentNotInVisit(
        AppointmentStatus status)
    {
        var harness = new Harness();
        harness.Checker.Result = AccessGrantCheckResult.Allowed(CreateGrant());
        harness.Store.Context = new DiagnosisWriteContext(status, MedicalRecordId);

        var result = await harness.Service.CreateDiagnosisAsync(
            AppointmentId,
            DoctorUserId,
            new CreateDiagnosisRequest { Description = "Acute bronchitis" },
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(DiagnosisWriteFailure.AppointmentNotInVisit, result.Failure);
        Assert.Empty(harness.Store.Diagnoses);
        Assert.Empty(harness.Audit.Entries);
        Assert.Equal(0, harness.Store.SaveCount);
    }

    [Fact]
    public async Task CreateDiagnosisAsync_WhenTheVisitIsCompletedButTheGrantIsActive_RecordsTheDiagnosis()
    {
        // ADR-0005: completion keeps the grant active for 15 minutes, so the doctor can finish the record.
        var harness = new Harness();
        harness.Checker.Result = AccessGrantCheckResult.Allowed(CreateGrant());
        harness.Store.Context = new DiagnosisWriteContext(AppointmentStatus.Completed, MedicalRecordId);

        var result = await harness.Service.CreateDiagnosisAsync(
            AppointmentId,
            DoctorUserId,
            new CreateDiagnosisRequest { Description = "Acute bronchitis" },
            IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Single(harness.Store.Diagnoses);
        Assert.Equal(1, harness.Store.SaveCount);
    }

    [Fact]
    public async Task CreateDiagnosisAsync_WhenTheMedicalRecordIsMissing_ReturnsMedicalRecordNotFound()
    {
        var harness = new Harness();
        harness.Checker.Result = AccessGrantCheckResult.Allowed(CreateGrant());
        harness.Store.Context = new DiagnosisWriteContext(AppointmentStatus.InVisit, MedicalRecordId: null);

        var result = await harness.Service.CreateDiagnosisAsync(
            AppointmentId,
            DoctorUserId,
            new CreateDiagnosisRequest { Description = "Acute bronchitis" },
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(DiagnosisWriteFailure.MedicalRecordNotFound, result.Failure);
        Assert.Empty(harness.Store.Diagnoses);
        Assert.Equal(0, harness.Store.SaveCount);
    }

    [Fact]
    public async Task CreateDiagnosisAsync_WhenTheGrantDoesNotMatchTheRouteAppointment_ReturnsScopeMismatch()
    {
        var harness = new Harness();
        harness.Checker.Result = AccessGrantCheckResult.Allowed(CreateGrant(appointmentId: AppointmentId + 1));

        var result = await harness.Service.CreateDiagnosisAsync(
            AppointmentId,
            DoctorUserId,
            new CreateDiagnosisRequest { Description = "Acute bronchitis" },
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(DiagnosisWriteFailure.ScopeMismatch, result.Failure);
        Assert.Equal(0, harness.Store.SaveCount);
    }

    [Fact]
    public async Task CreatePrescriptionAsync_WhenTheDiagnosisIsInScope_StoresTheEncryptedTextAndAuditRow()
    {
        var harness = new Harness();
        var grant = CreateGrant();
        harness.Checker.Result = AccessGrantCheckResult.Allowed(grant);
        harness.ScopeReader.Scope = new DiagnosisScope(DiagnosisId, AppointmentId, MedicalRecordId, PatientId);
        harness.Store.Context = new DiagnosisWriteContext(AppointmentStatus.InVisit, MedicalRecordId);

        var result = await harness.Service.CreatePrescriptionAsync(
            DiagnosisId,
            DoctorUserId,
            new CreatePrescriptionRequest
            {
                MedicineName = "Amoxicillin",
                Dosage = "500 mg twice a day",
                Notes = "Take after food"
            },
            IpAddress);

        Assert.True(result.IsSuccess);
        var prescription = Assert.Single(harness.Store.Prescriptions);
        Assert.Equal(DiagnosisId, prescription.DiagnosisId);
        Assert.Equal("cipher:Prescription:Amoxicillin", prescription.MedicineName);
        Assert.Equal("cipher:Prescription:500 mg twice a day", prescription.Dosage);
        Assert.Equal("cipher:Prescription:Take after food", prescription.Notes);
        Assert.Equal(1, harness.Store.SaveCount);

        Assert.All(harness.Protector.Calls, call => Assert.Equal(ClinicalTextPurpose.Prescription, call.Purpose));
        Assert.Equal(3, harness.Protector.Calls.Count);

        var audit = Assert.Single(harness.Audit.Entries);
        Assert.Equal(DoctorUserId, audit.UserId);
        Assert.Equal(PatientId, audit.PatientId);
        Assert.Equal(AuditAction.PrescriptionCreated, audit.Action);
        Assert.Equal(AuditEntityType.Prescription, audit.EntityType);
        Assert.Equal(prescription.Id.ToString(), audit.EntityId);
        Assert.Equal(grant.Id, audit.AccessGrantId);

        Assert.Equal(prescription.Id, result.Prescription!.Id);
        Assert.Equal(DiagnosisId, result.Prescription.DiagnosisId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreatePrescriptionAsync_WithBlankNotes_StoresNull(string? notes)
    {
        var harness = new Harness();
        harness.Checker.Result = AccessGrantCheckResult.Allowed(CreateGrant());
        harness.ScopeReader.Scope = new DiagnosisScope(DiagnosisId, AppointmentId, MedicalRecordId, PatientId);
        harness.Store.Context = new DiagnosisWriteContext(AppointmentStatus.InVisit, MedicalRecordId);

        var result = await harness.Service.CreatePrescriptionAsync(
            DiagnosisId,
            DoctorUserId,
            new CreatePrescriptionRequest
            {
                MedicineName = "Amoxicillin",
                Dosage = "500 mg twice a day",
                Notes = notes
            },
            IpAddress);

        Assert.True(result.IsSuccess);
        var prescription = Assert.Single(harness.Store.Prescriptions);
        Assert.Null(prescription.Notes);
        Assert.Equal(2, harness.Protector.Calls.Count);
    }

    [Fact]
    public async Task CreatePrescriptionAsync_WhenTheDiagnosisDoesNotExist_ReturnsScopeMismatch()
    {
        var harness = new Harness();
        harness.ScopeReader.Scope = null;

        var result = await harness.Service.CreatePrescriptionAsync(
            DiagnosisId,
            DoctorUserId,
            new CreatePrescriptionRequest
            {
                MedicineName = "Amoxicillin",
                Dosage = "500 mg twice a day"
            },
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(DiagnosisWriteFailure.ScopeMismatch, result.Failure);
        Assert.False(harness.Checker.WasCalled);
        Assert.Equal(0, harness.Store.SaveCount);
    }

    [Fact]
    public async Task CreatePrescriptionAsync_WhenTheGrantIsRefused_ReturnsGrantNotActive()
    {
        var harness = new Harness();
        harness.ScopeReader.Scope = new DiagnosisScope(DiagnosisId, AppointmentId, MedicalRecordId, PatientId);
        harness.Checker.Result = AccessGrantCheckResult.Denied(AccessGrantCheckFailure.GrantEnded);

        var result = await harness.Service.CreatePrescriptionAsync(
            DiagnosisId,
            DoctorUserId,
            new CreatePrescriptionRequest
            {
                MedicineName = "Amoxicillin",
                Dosage = "500 mg twice a day"
            },
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(DiagnosisWriteFailure.GrantNotActive, result.Failure);
        Assert.Empty(harness.Store.Prescriptions);
        Assert.Equal(0, harness.Store.SaveCount);
    }

    [Fact]
    public async Task CreatePrescriptionAsync_WhenTheDiagnosisBelongsToAnotherPatient_ReturnsScopeMismatch()
    {
        var harness = new Harness();
        harness.ScopeReader.Scope = new DiagnosisScope(DiagnosisId, AppointmentId, MedicalRecordId, OtherPatientId);
        harness.Checker.Result = AccessGrantCheckResult.Allowed(CreateGrant());

        var result = await harness.Service.CreatePrescriptionAsync(
            DiagnosisId,
            DoctorUserId,
            new CreatePrescriptionRequest
            {
                MedicineName = "Amoxicillin",
                Dosage = "500 mg twice a day"
            },
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(DiagnosisWriteFailure.ScopeMismatch, result.Failure);
        Assert.Equal(0, harness.Store.SaveCount);
    }

    [Fact]
    public async Task CreatePrescriptionAsync_WhenTheDiagnosisBelongsToAnotherAppointment_ReturnsScopeMismatch()
    {
        var harness = new Harness();
        harness.ScopeReader.Scope = new DiagnosisScope(
            DiagnosisId,
            AppointmentId + 1,
            MedicalRecordId,
            PatientId);
        harness.Checker.Result = AccessGrantCheckResult.Allowed(CreateGrant());

        var result = await harness.Service.CreatePrescriptionAsync(
            DiagnosisId,
            DoctorUserId,
            new CreatePrescriptionRequest
            {
                MedicineName = "Amoxicillin",
                Dosage = "500 mg twice a day"
            },
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(DiagnosisWriteFailure.ScopeMismatch, result.Failure);
        Assert.Equal(0, harness.Store.SaveCount);
    }

    [Fact]
    public async Task CreatePrescriptionAsync_WhenTheVisitIsNotOpen_ReturnsAppointmentNotInVisit()
    {
        var harness = new Harness();
        harness.ScopeReader.Scope = new DiagnosisScope(DiagnosisId, AppointmentId, MedicalRecordId, PatientId);
        harness.Checker.Result = AccessGrantCheckResult.Allowed(CreateGrant());
        harness.Store.Context = new DiagnosisWriteContext(AppointmentStatus.CheckedIn, MedicalRecordId);

        var result = await harness.Service.CreatePrescriptionAsync(
            DiagnosisId,
            DoctorUserId,
            new CreatePrescriptionRequest
            {
                MedicineName = "Amoxicillin",
                Dosage = "500 mg twice a day"
            },
            IpAddress);

        Assert.False(result.IsSuccess);
        Assert.Equal(DiagnosisWriteFailure.AppointmentNotInVisit, result.Failure);
        Assert.Empty(harness.Store.Prescriptions);
        Assert.Equal(0, harness.Store.SaveCount);
    }

    [Fact]
    public async Task CreatePrescriptionAsync_WhenTheVisitIsCompletedButTheGrantIsActive_AddsThePrescription()
    {
        var harness = new Harness();
        harness.ScopeReader.Scope = new DiagnosisScope(DiagnosisId, AppointmentId, MedicalRecordId, PatientId);
        harness.Checker.Result = AccessGrantCheckResult.Allowed(CreateGrant());
        harness.Store.Context = new DiagnosisWriteContext(AppointmentStatus.Completed, MedicalRecordId);

        var result = await harness.Service.CreatePrescriptionAsync(
            DiagnosisId,
            DoctorUserId,
            new CreatePrescriptionRequest
            {
                MedicineName = "Amoxicillin",
                Dosage = "500 mg twice a day"
            },
            IpAddress);

        Assert.True(result.IsSuccess);
        Assert.Single(harness.Store.Prescriptions);
        Assert.Equal(1, harness.Store.SaveCount);
    }

    private static AccessGrant CreateGrant(
        int appointmentId = AppointmentId,
        Guid? patientId = null)
    {
        var appointment = new Appointment
        {
            Id = appointmentId,
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

        return AccessGrant.Create(request, appointment, Now.UtcDateTime);
    }

    private sealed class Harness
    {
        public FakeAccessGrantChecker Checker { get; } = new();

        public FakeDiagnosisScopeReader ScopeReader { get; } = new();

        public FakeDiagnosisWriteStore Store { get; } = new();

        public FakeClinicalTextProtector Protector { get; } = new();

        public FakeAuditWriter Audit { get; } = new();

        public DiagnosisWriteService Service { get; }

        public Harness()
        {
            Service = new DiagnosisWriteService(
                Checker,
                ScopeReader,
                Store,
                Protector,
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

    private sealed class FakeAccessGrantChecker : IAccessGrantChecker
    {
        public AccessGrantCheckResult Result { get; set; } =
            AccessGrantCheckResult.Denied(AccessGrantCheckFailure.GrantNotFound);

        public bool WasCalled { get; private set; }

        public Guid LastDoctorUserId { get; private set; }

        public int LastAppointmentId { get; private set; }

        public Task<AccessGrantCheckResult> RequireActiveGrantAsync(
            Guid doctorUserId,
            int appointmentId,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastDoctorUserId = doctorUserId;
            LastAppointmentId = appointmentId;

            return Task.FromResult(Result);
        }

        // The write path this harness covers resolves access from an appointment, so the record-scoped overload is
        // never used here. It fails loudly instead of quietly returning a grant, so a future misuse cannot pass.
        public Task<AccessGrantCheckResult> RequireActiveGrantForPatientAsync(
            Guid doctorUserId,
            Guid patientUserId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This harness only covers the appointment-scoped grant check.");
    }

    private sealed class FakeDiagnosisScopeReader : IDiagnosisScopeReader
    {
        public DiagnosisScope? Scope { get; set; }

        public bool WasCalled { get; private set; }

        public Guid LastDiagnosisId { get; private set; }

        public Task<DiagnosisScope?> FindAsync(
            Guid diagnosisId,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            LastDiagnosisId = diagnosisId;

            return Task.FromResult(Scope);
        }
    }

    private sealed class FakeDiagnosisWriteStore : IDiagnosisWriteStore
    {
        public DiagnosisWriteContext? Context { get; set; }

        public List<Diagnosis> Diagnoses { get; } = [];

        public List<Prescription> Prescriptions { get; } = [];

        public int SaveCount { get; private set; }

        public Task<DiagnosisWriteContext?> LoadForAppointmentAsync(
            int appointmentId,
            Guid patientId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Context);

        public void Add(Diagnosis diagnosis) => Diagnoses.Add(diagnosis);

        public void Add(Prescription prescription) => Prescriptions.Add(prescription);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed record ProtectCall(ClinicalTextPurpose Purpose, string Plaintext);

    private sealed class FakeClinicalTextProtector : IClinicalTextProtector
    {
        public List<ProtectCall> Calls { get; } = [];

        public string Protect(ClinicalTextPurpose purpose, string plaintext)
        {
            Calls.Add(new ProtectCall(purpose, plaintext));

            return $"cipher:{purpose}:{plaintext}";
        }

        public string Unprotect(ClinicalTextPurpose purpose, string ciphertext) =>
            throw new NotSupportedException("The write path never decrypts.");
    }

    private sealed class FakeAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public AuditLog Add(AuditEntry entry)
        {
            Entries.Add(entry);

            return null!;
        }

        public Task<AuditLog> WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The write path stages audit rows in its own unit of work.");
    }
}

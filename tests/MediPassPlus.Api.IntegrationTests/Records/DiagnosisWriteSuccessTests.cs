using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Application.Privacy;
using MediPassPlus.Application.Records;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Api.IntegrationTests.Records;

/// <summary>
/// The happy paths for the doctor write path over HTTP: an active grant produces a <c>201</c>, the stored row holds
/// ciphertext rather than the text the caller sent, the audit entry is committed, and the append-only rule holds
/// (FR-REC-03, FR-REC-04, NFR-SEC-05, FR-ACS-07, ADR-0005, ADR-0004 decision 7).
/// </summary>
/// <remarks>
/// The harness hosts the real protector over an ephemeral Data Protection ring, so these tests exercise the same
/// encryption path as a deployment. Every value is synthetic: no real patient data is stored or asserted.
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
public sealed class DiagnosisWriteSuccessTests : IClassFixture<MediPassApiFactory>
{
    private const string DiagnosisText = "Synthetic acute bronchitis, follow-up in two weeks.";
    private const string MedicineName = "Synthetic medicine";
    private const string Dosage = "1 tablet three times a day";
    private const string Notes = "Synthetic note: take after food.";

    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with its own API factory and database.</summary>
    /// <param name="factory">The class fixture that hosts this class's API and database.</param>
    public DiagnosisWriteSuccessTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateDiagnosis_WithAnActiveGrant_ReturnsCreatedAndStoresCiphertext()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);
        var before = DateTime.UtcNow;

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/diagnoses",
            new CreateDiagnosisRequest { Description = DiagnosisText });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);

        var created = await response.Content.ReadFromJsonAsync<DiagnosisResponse>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(scenario.Visit.Appointment.Id, created.AppointmentId);
        Assert.InRange(created.DiagnosedAtUtc, before.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        await using var db = _factory.CreateDbContext();

        var stored = await db.Diagnoses.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        Assert.Equal(scenario.Record.Id, stored.MedicalRecordId);
        Assert.Equal(scenario.Visit.Appointment.Id, stored.AppointmentId);
        Assert.NotEqual(DiagnosisText, stored.Description);
        Assert.DoesNotContain(DiagnosisText, stored.Description, StringComparison.Ordinal);
        Assert.Equal(DiagnosisText, Unprotect(ClinicalTextPurpose.Diagnosis, stored.Description));

        // Append only: exactly one row was added and the seeded row is byte-for-byte untouched.
        Assert.Equal(
            2,
            await db.Diagnoses.AsNoTracking().CountAsync(row => row.AppointmentId == stored.AppointmentId));
        var seeded = await db.Diagnoses.AsNoTracking().SingleAsync(row => row.Id == scenario.Diagnosis.Id);
        Assert.Equal(scenario.Diagnosis.Description, seeded.Description);

        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(log => log.EntityId == created.Id.ToString());
        Assert.Equal(AuditAction.DiagnosisCreated, audit.Action);
        Assert.Equal(AuditEntityType.Diagnosis, audit.EntityType);
        Assert.Equal(scenario.Visit.PatientUser.Id, audit.PatientId);
        Assert.Equal(scenario.Visit.DoctorUser.Id, audit.UserId);
        Assert.Equal(scenario.Grant!.Id, audit.AccessGrantId);
    }

    [Fact]
    public async Task CreateDiagnosis_WhenTheVisitIsCompletedButTheGrantIsStillActive_ReturnsCreated()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync(AppointmentStatus.Completed);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/diagnoses",
            new CreateDiagnosisRequest { Description = DiagnosisText });

        // MP-46 says record writes happen only in InVisit, but Accepted ADR-0005 and FR-ACS-07 extend the write
        // window through the 15-minute post-completion grace while the grant is still active. The ADR wins until
        // the ticket text is reconciled with the team.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>An active grant does not allow a write before the visit reaches <c>InVisit</c>.</summary>
    /// <param name="status">The appointment status under test.</param>
    [Theory]
    [InlineData(AppointmentStatus.Booked)]
    [InlineData(AppointmentStatus.CheckedIn)]
    public async Task CreateDiagnosis_WhenTheVisitHasNotStarted_ReturnsBadRequest(AppointmentStatus status)
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync(status);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/diagnoses",
            new CreateDiagnosisRequest { Description = DiagnosisText });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        Assert.Equal(
            1,
            await db.Diagnoses.AsNoTracking().CountAsync(row => row.AppointmentId == scenario.Visit.Appointment.Id));
    }

    /// <summary>An active grant does not allow a prescription before the visit reaches <c>InVisit</c>.</summary>
    /// <param name="status">The appointment status under test.</param>
    [Theory]
    [InlineData(AppointmentStatus.Booked)]
    [InlineData(AppointmentStatus.CheckedIn)]
    public async Task AddPrescription_WhenTheVisitHasNotStarted_ReturnsBadRequest(AppointmentStatus status)
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync(status);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/diagnoses/{scenario.Diagnosis.Id}/prescriptions",
            new CreatePrescriptionRequest
            {
                MedicineName = MedicineName,
                Dosage = Dosage,
                Notes = Notes
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        Assert.False(await db.Prescriptions.AsNoTracking()
            .AnyAsync(row => row.DiagnosisId == scenario.Diagnosis.Id));
    }

    [Fact]
    public async Task AddPrescription_WithAnActiveGrant_ReturnsCreatedAndStoresCiphertext()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/diagnoses/{scenario.Diagnosis.Id}/prescriptions",
            new CreatePrescriptionRequest
            {
                MedicineName = MedicineName,
                Dosage = Dosage,
                Notes = Notes
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);

        var created = await response.Content.ReadFromJsonAsync<PrescriptionResponse>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(scenario.Diagnosis.Id, created.DiagnosisId);

        await using var db = _factory.CreateDbContext();

        var stored = await db.Prescriptions.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        Assert.Equal(scenario.Diagnosis.Id, stored.DiagnosisId);
        Assert.NotEqual(MedicineName, stored.MedicineName);
        Assert.NotEqual(Dosage, stored.Dosage);
        Assert.NotNull(stored.Notes);
        Assert.NotEqual(Notes, stored.Notes);
        Assert.Equal(MedicineName, Unprotect(ClinicalTextPurpose.Prescription, stored.MedicineName));
        Assert.Equal(Dosage, Unprotect(ClinicalTextPurpose.Prescription, stored.Dosage));
        Assert.Equal(Notes, Unprotect(ClinicalTextPurpose.Prescription, stored.Notes!));

        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(log => log.EntityId == created.Id.ToString());
        Assert.Equal(AuditAction.PrescriptionCreated, audit.Action);
        Assert.Equal(AuditEntityType.Prescription, audit.EntityType);
        Assert.Equal(scenario.Visit.PatientUser.Id, audit.PatientId);
        Assert.Equal(scenario.Visit.DoctorUser.Id, audit.UserId);
        Assert.Equal(scenario.Grant!.Id, audit.AccessGrantId);
    }

    private string Unprotect(ClinicalTextPurpose purpose, string ciphertext) =>
        _factory.Services
            .GetRequiredService<IClinicalTextProtector>()
            .Unprotect(purpose, ciphertext);
}

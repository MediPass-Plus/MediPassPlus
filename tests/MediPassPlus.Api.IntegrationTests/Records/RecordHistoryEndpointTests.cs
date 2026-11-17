using System.Net;
using System.Text.Json;
using MediPassPlus.Application.Records;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests.Records;

/// <summary>
/// The HTTP contract of both history endpoints: the doctor reads only while an active grant exists, the patient reads
/// their own record without a grant and never another patient's, clinical text comes back decrypted, every successful
/// read writes a <c>RecordRead</c> audit row, and both lists are paged (FR-REC-02, FR-REC-07, FR-PRV-01, FR-PRV-02,
/// NFR-PRF-04, NFR-PRI-01, ADR-0004, ADR-0005).
/// </summary>
/// <remarks>
/// Every value is synthetic. The harness hosts the real protector over an ephemeral Data Protection ring, so the
/// stored rows hold the same ciphertext shape a deployment writes.
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
public sealed class RecordHistoryEndpointTests : IClassFixture<MediPassApiFactory>
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with its own API factory and database.</summary>
    /// <param name="factory">The class fixture that hosts this class's API and database.</param>
    public RecordHistoryEndpointTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DoctorHistory_WithAnActiveGrant_ReturnsTheDecryptedRecord()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.GetAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);

        var raw = await response.Content.ReadAsStringAsync();
        var history = JsonSerializer.Deserialize<RecordHistoryResponse>(raw, WebJson);
        Assert.NotNull(history);

        Assert.Equal(scenario.Record.Id, history.MedicalRecordId);
        Assert.InRange(
            history.RecordCreatedAtUtc,
            scenario.Record.CreatedAtUtc.AddSeconds(-1),
            scenario.Record.CreatedAtUtc.AddSeconds(1));

        Assert.Equal(scenario.Diagnoses.Count, history.Diagnoses.TotalCount);
        Assert.Equal(
            scenario.Diagnoses.Select(diagnosis => diagnosis.Id).ToArray(),
            history.Diagnoses.Items.Select(diagnosis => diagnosis.Id).ToArray());
        Assert.Equal(
            $"{MediPassApiFactory.HistoryDiagnosisPrefix}0",
            history.Diagnoses.Items[0].Description);
        Assert.DoesNotContain(scenario.Diagnoses[0].Description, raw, StringComparison.Ordinal);

        var first = history.Diagnoses.Items[0];
        Assert.Equal(scenario.Prescriptions.Count, first.Prescriptions.Count);

        // The list is a set of prescriptions, so the assertions match by medicine name instead of position.
        var withoutNotes = Assert.Single(first.Prescriptions.Where(
            item => item.MedicineName == $"{MediPassApiFactory.HistoryMedicinePrefix}0"));
        Assert.Equal($"{MediPassApiFactory.HistoryDosagePrefix}0", withoutNotes.Dosage);
        Assert.Null(withoutNotes.Notes);

        var withNotes = Assert.Single(first.Prescriptions.Where(
            item => item.MedicineName == $"{MediPassApiFactory.HistoryMedicinePrefix}1"));
        Assert.Equal($"{MediPassApiFactory.HistoryDosagePrefix}1", withNotes.Dosage);
        Assert.Equal($"{MediPassApiFactory.HistoryNotesPrefix}1", withNotes.Notes);

        Assert.Equal(scenario.Reports.Count, history.Reports.TotalCount);
        Assert.Equal(
            scenario.Reports.Select(report => report.Id).ToArray(),
            history.Reports.Items.Select(report => report.Id).ToArray());
        Assert.All(history.Reports.Items, item => Assert.Equal("application/pdf", item.ContentType));

        // The random storage name is an internal detail and must never reach a caller.
        Assert.DoesNotContain("stored-", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoctorHistory_WithoutGrant_ReturnsForbiddenAndReadsNothing()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync(includeActiveGrant: false);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.GetAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        Assert.False(await db.AuditLogs.AsNoTracking()
            .AnyAsync(log => log.EntityId == scenario.Record.Id.ToString()));
    }

    [Fact]
    public async Task DoctorHistory_WhenTheGrantHasExpired_ReturnsForbidden()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync(
            grantedAtUtc: DateTime.UtcNow.AddHours(-3));
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.GetAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DoctorHistory_WhenThePatientEndedTheGrant_ReturnsForbidden()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync();
        await EndGrantAsync(scenario);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.GetAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DoctorHistory_ForAnotherDoctorsAppointment_ReturnsForbidden()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync();
        var otherDoctor = await CreateOtherDoctorAsync(scenario.Visit.Clinic.Id);
        using var client = _factory.CreateAuthenticatedClient(otherDoctor);

        using var response = await client.GetAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DoctorHistory_RecordsTheReadWithTheGrant()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.GetAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/history");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(log => log.EntityId == scenario.Record.Id.ToString());

        Assert.Equal(AuditAction.RecordRead, audit.Action);
        Assert.Equal(AuditEntityType.MedicalRecord, audit.EntityType);
        Assert.Equal(scenario.Visit.DoctorUser.Id, audit.UserId);
        Assert.Equal(scenario.Visit.PatientUser.Id, audit.PatientId);
        Assert.Equal(scenario.Grant!.Id, audit.AccessGrantId);
    }

    [Fact]
    public async Task DoctorHistory_IgnoresAPatientIdInTheQueryString()
    {
        var first = await _factory.CreateRecordHistoryScenarioAsync();
        var second = await _factory.CreateRecordHistoryScenarioAsync(includeActiveGrant: false);
        using var client = _factory.CreateAuthenticatedClient(first.Visit.DoctorUser);

        // The patient is taken from the verified grant. A caller-supplied identifier must change nothing.
        var history = await ReadHistoryAsync(
            client,
            $"/api/appointments/{first.Visit.Appointment.Id}/history?patientId={second.Visit.PatientUser.Id}");

        Assert.Equal(first.Record.Id, history.MedicalRecordId);

        var returnedIds = history.Diagnoses.Items.Select(diagnosis => diagnosis.Id).ToHashSet();
        Assert.All(second.Diagnoses, diagnosis => Assert.DoesNotContain(diagnosis.Id, returnedIds));
    }

    [Fact]
    public async Task PatientHistory_WithoutAnyGrant_ReturnsOnlyThePatientsOwnRows()
    {
        var first = await _factory.CreateRecordHistoryScenarioAsync(includeActiveGrant: false);
        var second = await _factory.CreateRecordHistoryScenarioAsync(includeActiveGrant: false);
        using var client = _factory.CreateAuthenticatedClient(first.Visit.PatientUser);

        // A forged patient identifier is not part of the contract; the route must ignore it and stay self-scoped.
        using var response = await client.GetAsync(
            $"/api/me/history?patientId={second.Visit.PatientUser.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);

        var raw = await response.Content.ReadAsStringAsync();
        var history = JsonSerializer.Deserialize<RecordHistoryResponse>(raw, WebJson);
        Assert.NotNull(history);
        Assert.Equal(first.Record.Id, history.MedicalRecordId);

        var returnedIds = history.Diagnoses.Items.Select(diagnosis => diagnosis.Id).ToHashSet();
        Assert.All(second.Diagnoses, diagnosis => Assert.DoesNotContain(diagnosis.Id, returnedIds));
        Assert.Equal(
            $"{MediPassApiFactory.HistoryDiagnosisPrefix}0",
            history.Diagnoses.Items[0].Description);
        Assert.DoesNotContain(first.Diagnoses[0].Description, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PatientHistory_RecordsTheSelfReadWithoutAGrant()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync(includeActiveGrant: false);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);

        using var response = await client.GetAsync("/api/me/history");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(log => log.EntityId == scenario.Record.Id.ToString());

        Assert.Equal(AuditAction.RecordRead, audit.Action);
        Assert.Equal(scenario.Visit.PatientUser.Id, audit.UserId);
        Assert.Equal(scenario.Visit.PatientUser.Id, audit.PatientId);
        Assert.Null(audit.AccessGrantId);
    }

    [Fact]
    public async Task PatientHistory_WhenTheCallerIsADoctor_ReturnsForbidden()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.GetAsync("/api/me/history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DoctorHistory_WhenTheCallerIsAPatient_ReturnsForbidden()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);

        using var response = await client.GetAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task History_WithoutAToken_ReturnsUnauthorized()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync();
        using var client = _factory.CreateClient();

        using var doctorRoute = await client.GetAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/history");
        using var patientRoute = await client.GetAsync("/api/me/history");

        Assert.Equal(HttpStatusCode.Unauthorized, doctorRoute.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, patientRoute.StatusCode);
    }

    [Fact]
    public async Task DoctorHistory_PagesTheDiagnosesNewestFirst()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync(diagnosisCount: 3);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        var firstPage = await ReadHistoryAsync(
            client,
            $"/api/appointments/{scenario.Visit.Appointment.Id}/history?page=1&pageSize=2");
        var secondPage = await ReadHistoryAsync(
            client,
            $"/api/appointments/{scenario.Visit.Appointment.Id}/history?page=2&pageSize=2");

        Assert.Equal(3, firstPage.Diagnoses.TotalCount);
        Assert.Equal(
            scenario.Diagnoses.Take(2).Select(diagnosis => diagnosis.Id).ToArray(),
            firstPage.Diagnoses.Items.Select(diagnosis => diagnosis.Id).ToArray());
        Assert.Equal(3, secondPage.Diagnoses.TotalCount);
        Assert.Equal(
            scenario.Diagnoses[2].Id,
            Assert.Single(secondPage.Diagnoses.Items).Id);
    }

    [Fact]
    public async Task DoctorHistory_PagesTheReportsSeparately()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync(reportCount: 3);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        var history = await ReadHistoryAsync(
            client,
            $"/api/appointments/{scenario.Visit.Appointment.Id}/history?reportPage=2&reportPageSize=1");

        Assert.Equal(3, history.Reports.TotalCount);
        Assert.Equal(scenario.Reports[1].Id, Assert.Single(history.Reports.Items).Id);

        // The diagnoses page keeps its own default.
        Assert.Equal(1, history.Diagnoses.Page);
        Assert.Equal(20, history.Diagnoses.PageSize);
    }

    [Fact]
    public async Task History_RejectsAnInvalidPageSize()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);
        var route = $"/api/appointments/{scenario.Visit.Appointment.Id}/history";

        using var diagnosisPageTooLarge = await client.GetAsync($"{route}?pageSize=51");
        using var reportPageTooLarge = await client.GetAsync($"{route}?reportPageSize=51");
        using var emptyPageSize = await client.GetAsync($"{route}?pageSize=0");

        Assert.Equal(HttpStatusCode.BadRequest, diagnosisPageTooLarge.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reportPageTooLarge.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptyPageSize.StatusCode);
    }

    private static async Task<RecordHistoryResponse> ReadHistoryAsync(HttpClient client, string uri)
    {
        using var response = await client.GetAsync(uri);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        var history = JsonSerializer.Deserialize<RecordHistoryResponse>(raw, WebJson);

        Assert.NotNull(history);
        return history;
    }

    /// <summary>
    /// Ends the seeded grant through the patient's own endpoint, so the refusal is proven against a grant the API
    /// actually ended (FR-ACS-09).
    /// </summary>
    /// <param name="scenario">The scenario whose grant is ended.</param>
    private async Task EndGrantAsync(RecordHistoryScenario scenario)
    {
        Assert.NotNull(scenario.Grant);

        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);
        using var response = await client.PostAsync(
            $"/api/access-grants/{scenario.Grant!.Id}/end",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Seeds a second doctor in the same clinic, so the refusal is the ownership rule and not a missing profile.
    /// </summary>
    /// <param name="clinicId">The clinic the visit belongs to.</param>
    /// <returns>The other doctor's account and token.</returns>
    private async Task<TestUser> CreateOtherDoctorAsync(int clinicId)
    {
        var user = await _factory.CreateUserAsync(DefaultRoles.Doctor);

        await using var db = _factory.CreateDbContext();
        db.Doctors.Add(new Doctor
        {
            UserId = user.Id,
            Specialty = "Dermatology",
            ClinicId = clinicId
        });
        await db.SaveChangesAsync();

        return user;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediPassPlus.Application.Audit;
using MediPassPlus.Application.Common;
using MediPassPlus.Application.Records;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Api.IntegrationTests.Privacy;

/// <summary>
/// The HTTP contract of the patient's "who viewed my record" list and the audit coverage it stands on: patient-only
/// access, own rows only, newest first, paged, no medical content, reading the list is not audited, every medical
/// read/write leaves exactly one row, and no row can be updated or deleted (FR-PRV-02, FR-PRV-03, NFR-PRI-03,
/// NFR-PRI-05, NFR-PRF-04, US-32, UC-20, ADR-0004).
/// </summary>
/// <remarks>
/// Every value is synthetic. The class fixture hosts the real API and the real SQL Server schema, so the immutability
/// and coverage proofs run against the same store a deployment uses.
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
public sealed class MyAuditEndpointTests : IClassFixture<MediPassApiFactory>
{
    private static readonly string[] AllowedProperties =
    [
        "id",
        "userId",
        "action",
        "entityType",
        "entityId",
        "occurredAtUtc",
        "ipAddress",
        "accessGrantId"
    ];

    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with its own API factory and database.</summary>
    /// <param name="factory">The class fixture that hosts this class's API and database.</param>
    public MyAuditEndpointTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetMyAudit_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/me/audit");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(DefaultRoles.Doctor)]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task GetMyAudit_WithNonPatientRole_ReturnsForbidden(string role)
    {
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await client.GetAsync("/api/me/audit");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMyAudit_ReturnsOnlyThePatientsOwnRowsNewestFirst()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        var doctor = await _factory.CreateUserAsync(DefaultRoles.Doctor);
        var now = DateTimeOffset.UtcNow;

        var doctorRead = await SeedAuditAsync(
            patient.Id,
            doctor.Id,
            AuditAction.RecordRead,
            now.AddMinutes(-30));
        var selfRead = await SeedAuditAsync(
            patient.Id,
            patient.Id,
            AuditAction.RecordRead,
            now.AddMinutes(-5));
        var otherPatientRow = await SeedAuditAsync(
            otherPatient.Id,
            doctor.Id,
            AuditAction.RecordRead,
            now.AddMinutes(-1));

        using var client = _factory.CreateAuthenticatedClient(patient);
        using var response = await client.GetAsync("/api/me/audit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);

        var page = await ReadPageAsync(response);

        // The filter is the patient whose data was affected, so a doctor's read belongs to this patient's list.
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new[] { selfRead, doctorRead }, page.Items.Select(item => item.Id).ToArray());
        Assert.DoesNotContain(page.Items, item => item.Id == otherPatientRow);
        Assert.Equal(patient.Id, page.Items[0].UserId);
        Assert.Equal(doctor.Id, page.Items[1].UserId);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
    }

    [Fact]
    public async Task GetMyAudit_PagesThePatientsRowsAndValidatesThePage()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        var doctor = await _factory.CreateUserAsync(DefaultRoles.Doctor);
        var now = DateTimeOffset.UtcNow;

        var seeded = new List<Guid>();
        await using (var db = _factory.CreateDbContext())
        {
            for (var index = 0; index < 25; index++)
            {
                var log = AuditLog.Create(
                    doctor.Id,
                    patient.Id,
                    AuditAction.RecordRead,
                    AuditEntityType.MedicalRecord,
                    $"record-{index}",
                    "203.0.113.10",
                    now.AddMinutes(-index));
                seeded.Add(log.Id);
                db.AuditLogs.Add(log);
            }

            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateAuthenticatedClient(patient);

        using var firstResponse = await client.GetAsync("/api/me/audit");
        var firstPage = await ReadPageAsync(firstResponse);

        Assert.Equal(25, firstPage.TotalCount);
        Assert.Equal(20, firstPage.Items.Count);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(20, firstPage.PageSize);
        Assert.Equal(seeded.Take(20).ToArray(), firstPage.Items.Select(item => item.Id).ToArray());

        using var secondResponse = await client.GetAsync("/api/me/audit?page=2");
        var secondPage = await ReadPageAsync(secondResponse);

        Assert.Equal(25, secondPage.TotalCount);
        Assert.Equal(5, secondPage.Items.Count);
        Assert.Equal(2, secondPage.Page);
        Assert.Equal(seeded.Skip(20).ToArray(), secondPage.Items.Select(item => item.Id).ToArray());

        using var singleResponse = await client.GetAsync("/api/me/audit?page=3&pageSize=1");
        var singlePage = await ReadPageAsync(singleResponse);

        Assert.Equal(25, singlePage.TotalCount);
        Assert.Equal(seeded[2], Assert.Single(singlePage.Items).Id);

        using var tooLarge = await client.GetAsync("/api/me/audit?pageSize=51");
        using var emptySize = await client.GetAsync("/api/me/audit?pageSize=0");
        using var emptyPage = await client.GetAsync("/api/me/audit?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptySize.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, emptyPage.StatusCode);
    }

    [Fact]
    public async Task PatientSelfRead_WritesExactlyOneAuditRowAndAppearsInTheList()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync(includeActiveGrant: false);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);

        using var historyResponse = await client.GetAsync("/api/me/history");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);

        await using var db = _factory.CreateDbContext();
        var row = Assert.Single(await db.AuditLogs
            .AsNoTracking()
            .Where(audit => audit.PatientId == scenario.Visit.PatientUser.Id)
            .ToListAsync());

        Assert.Equal(AuditAction.RecordRead, row.Action);
        Assert.Equal(AuditEntityType.MedicalRecord, row.EntityType);
        Assert.Equal(scenario.Record.Id.ToString(), row.EntityId);
        Assert.Equal(scenario.Visit.PatientUser.Id, row.UserId);
        Assert.Null(row.AccessGrantId);

        using var listResponse = await client.GetAsync("/api/me/audit");
        var page = await ReadPageAsync(listResponse);

        var item = Assert.Single(page.Items);
        Assert.Equal(row.Id, item.Id);
        Assert.Equal(nameof(AuditAction.RecordRead), item.Action);
        Assert.Equal(scenario.Visit.PatientUser.Id, item.UserId);
        Assert.Null(item.AccessGrantId);
    }

    [Fact]
    public async Task DoctorRead_WritesExactlyOneAuditRowAndAppearsInThePatientsList()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync();
        using var doctorClient = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var historyResponse = await doctorClient.GetAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/history");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);

        await using var db = _factory.CreateDbContext();
        var row = Assert.Single(await db.AuditLogs
            .AsNoTracking()
            .Where(audit => audit.PatientId == scenario.Visit.PatientUser.Id)
            .ToListAsync());

        Assert.Equal(AuditAction.RecordRead, row.Action);
        Assert.Equal(scenario.Record.Id.ToString(), row.EntityId);
        Assert.Equal(scenario.Visit.DoctorUser.Id, row.UserId);
        Assert.Equal(scenario.Grant!.Id, row.AccessGrantId);

        using var patientClient = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);
        using var listResponse = await patientClient.GetAsync("/api/me/audit");
        var page = await ReadPageAsync(listResponse);

        var item = Assert.Single(page.Items);
        Assert.Equal(row.Id, item.Id);
        Assert.Equal(nameof(AuditAction.RecordRead), item.Action);
        Assert.Equal(scenario.Visit.DoctorUser.Id, item.UserId);
        Assert.Equal(scenario.Grant.Id, item.AccessGrantId);
    }

    [Fact]
    public async Task DoctorWrites_LeaveExactlyOneAuditRowPerActionAndAppearInThePatientsList()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        using var doctorClient = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var diagnosisResponse = await doctorClient.PostAsJsonAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/diagnoses",
            new CreateDiagnosisRequest { Description = "Synthetic diagnosis written by the audit coverage test." });
        Assert.Equal(HttpStatusCode.Created, diagnosisResponse.StatusCode);
        var diagnosis = await diagnosisResponse.Content.ReadFromJsonAsync<DiagnosisResponse>();
        Assert.NotNull(diagnosis);

        using var prescriptionResponse = await doctorClient.PostAsJsonAsync(
            $"/api/diagnoses/{scenario.Diagnosis.Id}/prescriptions",
            new CreatePrescriptionRequest
            {
                MedicineName = "Synthetic medicine",
                Dosage = "1 tablet daily",
                Notes = "Synthetic note written by the audit coverage test."
            });
        Assert.Equal(HttpStatusCode.Created, prescriptionResponse.StatusCode);
        var prescription = await prescriptionResponse.Content.ReadFromJsonAsync<PrescriptionResponse>();
        Assert.NotNull(prescription);

        await using var db = _factory.CreateDbContext();

        var diagnosisAudit = Assert.Single(await db.AuditLogs
            .AsNoTracking()
            .Where(audit => audit.EntityId == diagnosis.Id.ToString())
            .ToListAsync());
        Assert.Equal(AuditAction.DiagnosisCreated, diagnosisAudit.Action);
        Assert.Equal(AuditEntityType.Diagnosis, diagnosisAudit.EntityType);
        Assert.Equal(scenario.Visit.PatientUser.Id, diagnosisAudit.PatientId);
        Assert.Equal(scenario.Visit.DoctorUser.Id, diagnosisAudit.UserId);
        Assert.Equal(scenario.Grant!.Id, diagnosisAudit.AccessGrantId);

        var prescriptionAudit = Assert.Single(await db.AuditLogs
            .AsNoTracking()
            .Where(audit => audit.EntityId == prescription.Id.ToString())
            .ToListAsync());
        Assert.Equal(AuditAction.PrescriptionCreated, prescriptionAudit.Action);
        Assert.Equal(AuditEntityType.Prescription, prescriptionAudit.EntityType);
        Assert.Equal(scenario.Visit.PatientUser.Id, prescriptionAudit.PatientId);
        Assert.Equal(scenario.Visit.DoctorUser.Id, prescriptionAudit.UserId);
        Assert.Equal(scenario.Grant.Id, prescriptionAudit.AccessGrantId);

        using var patientClient = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);
        using var listResponse = await patientClient.GetAsync("/api/me/audit");
        var page = await ReadPageAsync(listResponse);

        Assert.Contains(
            page.Items,
            item => item.Id == diagnosisAudit.Id && item.Action == nameof(AuditAction.DiagnosisCreated));
        Assert.Contains(
            page.Items,
            item => item.Id == prescriptionAudit.Id && item.Action == nameof(AuditAction.PrescriptionCreated));
    }

    [Fact]
    public async Task GetMyAudit_DoesNotAuditItsOwnRead()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync(includeActiveGrant: false);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);

        using var historyResponse = await client.GetAsync("/api/me/history");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);

        await using var db = _factory.CreateDbContext();
        var before = await db.AuditLogs
            .AsNoTracking()
            .CountAsync(audit => audit.PatientId == scenario.Visit.PatientUser.Id);

        using var firstList = await client.GetAsync("/api/me/audit");
        using var secondList = await client.GetAsync("/api/me/audit?page=1&pageSize=5");
        using var pastTheEnd = await client.GetAsync("/api/me/audit?page=4");
        Assert.Equal(HttpStatusCode.OK, firstList.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondList.StatusCode);
        Assert.Equal(HttpStatusCode.OK, pastTheEnd.StatusCode);

        var after = await db.AuditLogs
            .AsNoTracking()
            .CountAsync(audit => audit.PatientId == scenario.Visit.PatientUser.Id);

        Assert.Equal(1, before);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task GetMyAudit_ReturnsIdentifiersOnlyAndNoMedicalContent()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync(
            diagnosisCount: 1,
            prescriptionCount: 1,
            reportCount: 0);
        using var doctorClient = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        // Produce one audited read whose row would leak clinical text if any content were ever stored on it.
        using var historyResponse = await doctorClient.GetAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/history");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);

        using var patientClient = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);
        using var listResponse = await patientClient.GetAsync("/api/me/audit");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var raw = await listResponse.Content.ReadAsStringAsync();
        var page = await ReadPageAsync(listResponse);
        Assert.NotEmpty(page.Items);

        // Neither the decrypted prefix nor the stored ciphertext of the seeded diagnosis may appear.
        Assert.DoesNotContain(MediPassApiFactory.HistoryDiagnosisPrefix, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(scenario.Diagnoses[0].Description, raw, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(raw);
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var properties = item.EnumerateObject().Select(property => property.Name).ToArray();
            Assert.Equal(AllowedProperties.OrderBy(name => name), properties.OrderBy(name => name));
        }
    }

    [Fact]
    public async Task AuditRows_CannotBeUpdatedOrDeletedThroughTheApi()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        var doctor = await _factory.CreateUserAsync(DefaultRoles.Doctor);
        var auditId = await SeedAuditAsync(
            patient.Id,
            doctor.Id,
            AuditAction.RecordRead,
            DateTimeOffset.UtcNow);

        using var client = _factory.CreateAuthenticatedClient(patient);

        using var put = await client.SendAsync(new HttpRequestMessage(HttpMethod.Put, "/api/me/audit"));
        using var patch = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/api/me/audit"));
        using var delete = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/me/audit"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, put.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, patch.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, delete.StatusCode);

        await AssertMutationIsRejectedAsync(
            context => context.Entry(
                context.AuditLogs.Single(audit => audit.Id == auditId)).State = EntityState.Modified);
        await AssertMutationIsRejectedAsync(
            context => context.AuditLogs.Remove(
                context.AuditLogs.Single(audit => audit.Id == auditId)));

        // The row is still readable through the application's own pipeline after both attempts.
        using var listResponse = await client.GetAsync("/api/me/audit");
        var page = await ReadPageAsync(listResponse);
        Assert.Equal(auditId, Assert.Single(page.Items).Id);
    }

    /// <summary>
    /// Applies a mutation to an audit row through the application's own <see cref="MediPassDbContext"/> and asserts the
    /// append-only interceptor refuses it before SQL Server sees the command (NFR-PRI-03, ADR-0004 decision 6).
    /// </summary>
    /// <param name="mutate">The tracked change to attempt.</param>
    private async Task AssertMutationIsRejectedAsync(Action<MediPassDbContext> mutate)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MediPassDbContext>();

        mutate(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SaveChangesAsync());
        Assert.Contains("append-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> SeedAuditAsync(
        Guid patientId,
        Guid userId,
        AuditAction action,
        DateTimeOffset occurredAtUtc,
        Guid? accessGrantId = null)
    {
        await using var db = _factory.CreateDbContext();
        var log = AuditLog.Create(
            userId,
            patientId,
            action,
            AuditEntityType.MedicalRecord,
            "record-1",
            "203.0.113.10",
            occurredAtUtc,
            accessGrantId);

        db.AuditLogs.Add(log);
        await db.SaveChangesAsync();

        return log.Id;
    }

    private static async Task<PagedResult<AuditLogEntryResponse>> ReadPageAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedResult<AuditLogEntryResponse>>();
        Assert.NotNull(page);

        return page;
    }
}

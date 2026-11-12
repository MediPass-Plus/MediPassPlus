using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP matrix for the patient's immediate end of a grant: authentication, the patient role, the wrong-patient
/// boundary, the two terminal refusals, the <c>AccessEnded</c> audit row, and the proof that the doctor's next gated
/// request is refused (FR-ACS-09, FR-ACS-11, NFR-SEC-10, NFR-PRI-02).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AccessGrantEndEndpointTests
{
    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with the shared API factory.</summary>
    /// <param name="factory">The collection fixture that hosts the API and database.</param>
    public AccessGrantEndEndpointTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task End_WithoutToken_ReturnsUnauthorized()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(AppointmentStatus.InVisit);
        var grant = await _factory.CreateGrantAsync(scenario.Appointment.Id);
        using var client = _factory.CreateClient();

        using var response = await client.PostAsync($"/api/access-grants/{grant.Id}/end", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(DefaultRoles.Doctor)]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task End_WithNonPatientRole_ReturnsForbidden(string role)
    {
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await client.PostAsync(
            $"/api/access-grants/{Guid.NewGuid()}/end",
            content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task End_WhenAnotherPatientOwnsTheGrant_ReturnsForbiddenAndLeavesItActive()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(AppointmentStatus.InVisit);
        var grant = await _factory.CreateGrantAsync(scenario.Appointment.Id);
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(otherPatient);

        using var response = await client.PostAsync($"/api/access-grants/{grant.Id}/end", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        var stored = await db.AccessGrants.AsNoTracking().SingleAsync(candidate => candidate.Id == grant.Id);
        Assert.Null(stored.EndedAtUtc);
        Assert.Null(stored.EndReason);
        Assert.False(await db.AuditLogs.AsNoTracking().AnyAsync(log => log.AccessGrantId == grant.Id));
    }

    [Fact]
    public async Task End_ForAnUnknownGrant_ReturnsNotFound()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(patient);

        using var response = await client.PostAsync(
            $"/api/access-grants/{Guid.NewGuid()}/end",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
    }

    [Fact]
    public async Task End_WhenThePatientOwnsAnActiveGrant_EndsItImmediatelyAndAudits()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(AppointmentStatus.InVisit);
        var grant = await _factory.CreateGrantAsync(scenario.Appointment.Id);
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);
        var before = DateTime.UtcNow;

        using var response = await client.PostAsync($"/api/access-grants/{grant.Id}/end", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);

        var ended = await response.Content.ReadFromJsonAsync<AccessGrantEndedResponse>();
        Assert.NotNull(ended);
        Assert.Equal(grant.Id, ended.GrantId);
        Assert.Equal(nameof(GrantEndReason.PatientEnded), ended.EndReason);
        Assert.InRange(ended.EndedAtUtc, before.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        await using var db = _factory.CreateDbContext();
        var stored = await db.AccessGrants.AsNoTracking().SingleAsync(candidate => candidate.Id == grant.Id);
        Assert.Equal(ended.EndedAtUtc, stored.EndedAtUtc);
        Assert.Equal(GrantEndReason.PatientEnded, stored.EndReason);
        Assert.False(stored.IsActiveAt(DateTime.UtcNow));

        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(log => log.AccessGrantId == grant.Id);
        Assert.Equal(AuditAction.AccessEnded, audit.Action);
        Assert.Equal(AuditEntityType.AccessGrant, audit.EntityType);
        Assert.Equal(scenario.PatientUser.Id, audit.UserId);
        Assert.Equal(scenario.PatientUser.Id, audit.PatientId);
        Assert.Equal(grant.Id.ToString(), audit.EntityId);
    }

    [Fact]
    public async Task End_WhenTheGrantAlreadyEnded_ReturnsConflictWithoutASecondAudit()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(AppointmentStatus.InVisit);
        var grant = await _factory.CreateGrantAsync(scenario.Appointment.Id);
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var first = await client.PostAsync($"/api/access-grants/{grant.Id}/end", content: null);
        using var second = await client.PostAsync($"/api/access-grants/{grant.Id}/end", content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);

        await using var db = _factory.CreateDbContext();
        Assert.Equal(
            1,
            await db.AuditLogs.AsNoTracking().CountAsync(log => log.AccessGrantId == grant.Id));
    }

    [Fact]
    public async Task End_WhenTheGrantReachedTheTwoHourLimit_ReturnsConflict()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(AppointmentStatus.InVisit);
        var grant = await _factory.CreateGrantAsync(
            scenario.Appointment.Id,
            grantedAtUtc: DateTime.UtcNow.AddHours(-3));
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsync($"/api/access-grants/{grant.Id}/end", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        var stored = await db.AccessGrants.AsNoTracking().SingleAsync(candidate => candidate.Id == grant.Id);
        Assert.Null(stored.EndedAtUtc);
        Assert.False(await db.AuditLogs.AsNoTracking().AnyAsync(log => log.AccessGrantId == grant.Id));
    }

    [Fact]
    public async Task End_DuringThePostCompletionGraceWindow_OverridesTheScheduledEnd()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(AppointmentStatus.InVisit);
        var grant = await _factory.CreateGrantAsync(scenario.Appointment.Id);
        using var doctorClient = _factory.CreateAuthenticatedClient(scenario.DoctorUser);
        using var patientClient = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var complete = await doctorClient.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/complete",
            content: null);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        using var end = await patientClient.PostAsync(
            $"/api/access-grants/{grant.Id}/end",
            content: null);

        Assert.Equal(HttpStatusCode.OK, end.StatusCode);
        var ended = await end.Content.ReadFromJsonAsync<AccessGrantEndedResponse>();
        Assert.NotNull(ended);
        Assert.Equal(nameof(GrantEndReason.PatientEnded), ended.EndReason);

        await using var db = _factory.CreateDbContext();
        var stored = await db.AccessGrants.AsNoTracking().SingleAsync(candidate => candidate.Id == grant.Id);
        Assert.Equal(ended.EndedAtUtc, stored.EndedAtUtc);
        Assert.Equal(GrantEndReason.PatientEnded, stored.EndReason);

        // Completion scheduled the end (doctor), then the patient overrode it (patient): both are audited.
        var audits = await db.AuditLogs.AsNoTracking()
            .Where(log => log.AccessGrantId == grant.Id)
            .ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Contains(audits, audit => audit.UserId == scenario.DoctorUser.Id);
        Assert.Contains(audits, audit => audit.UserId == scenario.PatientUser.Id);
    }

    [Fact]
    public async Task GatedRequest_AfterTheTwoHourLimit_ReturnsForbidden()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(AppointmentStatus.InVisit);
        await _factory.CreateGrantAsync(
            scenario.Appointment.Id,
            grantedAtUtc: DateTime.UtcNow.AddHours(-3));
        using var doctorClient = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        // The two-hour cap is computed from GrantedAtUtc, so the stored grant is already expired.
        using var response = await doctorClient.GetAsync(
            $"/api/test/medical-record-gate/{scenario.Appointment.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
    }

    [Fact]
    public async Task End_ThenTheDoctorsNextGatedRequest_ReturnsForbidden()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(AppointmentStatus.InVisit);
        var grant = await _factory.CreateGrantAsync(scenario.Appointment.Id);
        using var doctorClient = _factory.CreateAuthenticatedClient(scenario.DoctorUser);
        using var patientClient = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        // The active grant allows the doctor's gated request.
        using var allowed = await doctorClient.GetAsync(
            $"/api/test/medical-record-gate/{scenario.Appointment.Id}");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        using var end = await patientClient.PostAsync(
            $"/api/access-grants/{grant.Id}/end",
            content: null);
        Assert.Equal(HttpStatusCode.OK, end.StatusCode);

        // The very next request is refused by the database-backed gate, not by a cached claim.
        using var denied = await doctorClient.GetAsync(
            $"/api/test/medical-record-gate/{scenario.Appointment.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var problem = await denied.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
    }
}

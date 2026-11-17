using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP negative matrix for start-visit: authentication, role, appointment scope, the Cairo "today" rule, the
/// appointment state machine and the rule that an approved or ended grant is never reused (FR-ACS-01, FR-ACS-02,
/// FR-ACS-06, FR-ACS-11, FR-BKG-08, NFR-SEC-10, ADR-0011).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class StartVisitEndpointTests : IClassFixture<MediPassApiFactory>
{
    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with its own API factory and database.</summary>
    /// <param name="factory">The class fixture that hosts this class's API and database.</param>
    public StartVisitEndpointTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>No bearer token means the endpoint is never reached.</summary>
    [Fact]
    public async Task StartVisit_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        using var response = await PostStartAsync(client, 42);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Only the Doctor role may start a visit.</summary>
    /// <param name="role">The non-doctor role under test.</param>
    [Theory]
    [InlineData(DefaultRoles.Patient)]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task StartVisit_WithNonDoctorRole_ReturnsForbidden(string role)
    {
        var scenario = await _factory.CreateVisitScenarioAsync(includePendingRequest: false);
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await PostStartAsync(client, scenario.Appointment.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An unknown appointment id is a 404 with the shared problem-details envelope.</summary>
    [Fact]
    public async Task StartVisit_ForAnUnknownAppointment_ReturnsNotFound()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(includePendingRequest: false);
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await PostStartAsync(client, 999_999);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
    }

    /// <summary>A doctor cannot start another doctor's appointment, and the refusal writes nothing.</summary>
    [Fact]
    public async Task StartVisit_ForAnotherDoctorsAppointment_ReturnsForbiddenAndWritesNothing()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(includePendingRequest: false);
        var other = await _factory.CreateVisitScenarioAsync(includePendingRequest: false);
        using var client = _factory.CreateAuthenticatedClient(other.DoctorUser);

        using var response = await PostStartAsync(client, scenario.Appointment.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingWasStartedAsync(scenario);
    }

    /// <summary>Only an appointment on the clinic-local today can be started.</summary>
    [Fact]
    public async Task StartVisit_WhenTheAppointmentIsNotToday_ReturnsBadRequest()
    {
        var tomorrow = _factory.ClinicDateAtNineUtc(_factory.ClinicToday.AddDays(1));
        var scenario = await _factory.CreateVisitScenarioAsync(
            includePendingRequest: false,
            slotStartUtc: tomorrow);
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await PostStartAsync(client, scenario.Appointment.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingWasStartedAsync(scenario);
    }

    /// <summary>A visit that is not checked in or already in visit cannot be started.</summary>
    /// <param name="status">The appointment status under test.</param>
    [Theory]
    [InlineData(AppointmentStatus.Booked)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    public async Task StartVisit_WhenTheAppointmentIsNotCheckedIn_ReturnsBadRequest(AppointmentStatus status)
    {
        var scenario = await _factory.CreateVisitScenarioAsync(
            appointmentStatus: status,
            includePendingRequest: false);
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await PostStartAsync(client, scenario.Appointment.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingWasStartedAsync(scenario);
    }

    /// <summary>
    /// Re-issuing the QR for an already started visit is the UC-11 A1 retry: it reuses the pending request, revokes
    /// the old credential and never creates a second request.
    /// </summary>
    [Fact]
    public async Task StartVisit_WhenAlreadyInVisitWithoutAGrant_ReissuesTheQrWithoutADuplicateRequest()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(AppointmentStatus.InVisit);
        var request = RequireRequest(scenario);
        var oldCredential = scenario.Credential
            ?? throw new InvalidOperationException("The scenario has no QR credential.");
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await PostStartAsync(client, scenario.Appointment.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var start = await response.Content.ReadFromJsonAsync<StartVisitResponse>();
        Assert.NotNull(start);
        Assert.Equal(request.Id, start.AccessRequestId);
        Assert.NotEqual(scenario.QrToken, start.QrToken);

        await using var db = _factory.CreateDbContext();
        Assert.Equal(
            1,
            await db.AccessRequests.CountAsync(candidate => candidate.AppointmentId == scenario.Appointment.Id));

        var credentials = await db.VisitAccessCredentials.AsNoTracking()
            .Where(candidate => candidate.AccessRequestId == request.Id
                && candidate.Type == VisitCredentialType.QrToken)
            .ToListAsync();
        Assert.Equal(2, credentials.Count);
        Assert.NotNull(credentials.Single(candidate => candidate.Id == oldCredential.Id).RevokedAtUtc);
        var fresh = credentials.Single(candidate => candidate.Id != oldCredential.Id);
        Assert.Null(fresh.RevokedAtUtc);
        Assert.Equal(QrTokenGenerator.Hash(start.QrToken), fresh.SecretHash);

        var appointment = await db.Appointments.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == scenario.Appointment.Id);
        Assert.Equal(AppointmentStatus.InVisit, appointment.Status);
    }

    /// <summary>
    /// An ended grant is never reused: after the patient ends it, start-visit answers 409 and issues no new request
    /// or QR, while the original creation and end audits remain (FR-ACS-11, ADR-0011).
    /// </summary>
    [Fact]
    public async Task StartVisit_AfterThePatientEndedTheGrant_ReturnsConflictAndIssuesNoNewQr()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(includePendingRequest: false);
        using var doctorClient = _factory.CreateAuthenticatedClient(scenario.DoctorUser);
        using var patientClient = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var startResponse = await PostStartAsync(doctorClient, scenario.Appointment.Id);
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var start = await startResponse.Content.ReadFromJsonAsync<StartVisitResponse>();
        Assert.NotNull(start);

        using var approveResponse = await patientClient.PostAsync(
            $"/api/access-requests/{start.AccessRequestId}/approve",
            content: null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var grant = await approveResponse.Content.ReadFromJsonAsync<AccessGrantResponse>();
        Assert.NotNull(grant);

        using var endResponse = await patientClient.PostAsync(
            $"/api/access-grants/{grant.GrantId}/end",
            content: null);
        Assert.Equal(HttpStatusCode.OK, endResponse.StatusCode);

        using var restartResponse = await PostStartAsync(doctorClient, scenario.Appointment.Id);

        Assert.Equal(HttpStatusCode.Conflict, restartResponse.StatusCode);
        var problem = await restartResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);

        await using var db = _factory.CreateDbContext();
        Assert.Equal(
            1,
            await db.AccessRequests.CountAsync(candidate => candidate.AppointmentId == scenario.Appointment.Id));
        Assert.Equal(
            1,
            await db.AccessGrants.CountAsync(candidate => candidate.AppointmentId == scenario.Appointment.Id));
        Assert.Equal(
            1,
            await db.VisitAccessCredentials.CountAsync(candidate =>
                candidate.AccessRequestId == start.AccessRequestId
                && candidate.Type == VisitCredentialType.QrToken));
        Assert.Equal(
            1,
            await db.AuditLogs.CountAsync(log =>
                log.PatientId == scenario.PatientUser.Id
                && log.Action == AuditAction.AccessRequestCreated));
        Assert.True(await db.AuditLogs.AsNoTracking().AnyAsync(log =>
            log.AccessGrantId == grant.GrantId && log.Action == AuditAction.AccessEnded));
    }

    /// <summary>
    /// A grant that reached its two-hour cap is also never reused: start-visit answers 409 instead of issuing a QR
    /// that could never be approved (FR-ACS-07, FR-ACS-11).
    /// </summary>
    [Fact]
    public async Task StartVisit_AfterTheGrantExpired_ReturnsConflictAndIssuesNoNewQr()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(includePendingRequest: false);
        var grant = await _factory.CreateGrantAsync(
            scenario.Appointment.Id,
            grantedAtUtc: DateTime.UtcNow.AddHours(-3));
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await PostStartAsync(client, scenario.Appointment.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);

        await using var db = _factory.CreateDbContext();
        var request = await db.AccessRequests.AsNoTracking()
            .SingleAsync(candidate => candidate.AppointmentId == scenario.Appointment.Id);
        Assert.False(await db.VisitAccessCredentials.AsNoTracking()
            .AnyAsync(candidate => candidate.AccessRequestId == request.Id
                && candidate.Type == VisitCredentialType.QrToken));

        var stored = await db.AccessGrants.AsNoTracking().SingleAsync(candidate => candidate.Id == grant.Id);
        Assert.Null(stored.EndedAtUtc);
        Assert.False(await db.AuditLogs.AsNoTracking()
            .AnyAsync(log => log.PatientId == scenario.PatientUser.Id));
    }

    private static Task<HttpResponseMessage> PostStartAsync(HttpClient client, int appointmentId) =>
        client.PostAsync($"/api/appointments/{appointmentId}/start-visit", content: null);

    private static AccessRequest RequireRequest(VisitScenario scenario) =>
        scenario.Request
        ?? throw new InvalidOperationException("The scenario has no pending access request.");

    private async Task AssertNothingWasStartedAsync(VisitScenario scenario)
    {
        await using var db = _factory.CreateDbContext();
        Assert.False(await db.AccessRequests.AsNoTracking()
            .AnyAsync(request => request.AppointmentId == scenario.Appointment.Id));
        Assert.False(await db.AuditLogs.AsNoTracking()
            .AnyAsync(log => log.PatientId == scenario.PatientUser.Id));

        var appointment = await db.Appointments.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == scenario.Appointment.Id);
        Assert.Equal(scenario.Appointment.Status, appointment.Status);
        Assert.Null(appointment.InVisitAtUtc);
    }
}

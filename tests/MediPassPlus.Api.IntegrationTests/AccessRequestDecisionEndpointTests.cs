using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Api.IntegrationTests.Infrastructure;
using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP matrix for the patient approve/reject endpoints: authentication, authorization, the wrong-patient boundary,
/// the success shape and the single-decision rule (NFR-SEC-10, FR-ACS-06, ADR-0011).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AccessRequestDecisionEndpointTests
{
    private readonly IntegrationTestHost _host;

    /// <summary>Initializes the test class with the shared host.</summary>
    public AccessRequestDecisionEndpointTests(IntegrationTestHost host)
    {
        _host = host;
    }

    /// <summary>No bearer token means the endpoint is never reached.</summary>
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task Decision_WithoutToken_ReturnsUnauthorized(string action)
    {
        using var client = _host.CreateClient();

        using var response = await client.PostAsync(
            $"/api/access-requests/{Guid.NewGuid()}/{action}",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Only the Patient role may answer an access request.</summary>
    [Theory]
    [InlineData(DefaultRoles.Doctor, "approve")]
    [InlineData(DefaultRoles.Doctor, "reject")]
    [InlineData(DefaultRoles.Staff, "approve")]
    [InlineData(DefaultRoles.Admin, "reject")]
    public async Task Decision_WithNonPatientRole_ReturnsForbidden(string role, string action)
    {
        var user = await _host.CreateUserAsync(role);
        using var client = await _host.CreateAuthenticatedClientAsync(user);

        using var response = await client.PostAsync(
            $"/api/access-requests/{Guid.NewGuid()}/{action}",
            content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An unknown request id is a 404 with the shared problem-details envelope.</summary>
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task Decision_ForUnknownRequest_ReturnsNotFound(string action)
    {
        var patient = await _host.CreateUserAsync(DefaultRoles.Patient);
        using var client = await _host.CreateAuthenticatedClientAsync(patient);

        using var response = await client.PostAsync(
            $"/api/access-requests/{Guid.NewGuid()}/{action}",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
    }

    /// <summary>A patient cannot answer a request for another patient's appointment.</summary>
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task Decision_ForAnotherPatientsRequest_ReturnsForbiddenAndChangesNothing(string action)
    {
        var scenario = await _host.CreateVisitScenarioAsync();
        var request = RequireRequest(scenario);
        var otherPatient = await _host.CreateUserAsync(DefaultRoles.Patient);
        using var client = await _host.CreateAuthenticatedClientAsync(otherPatient);

        using var response = await client.PostAsync(
            $"/api/access-requests/{request.Id}/{action}",
            content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(403, problem.Status);

        await using var db = _host.CreateDbContext();
        Assert.Equal(
            AccessRequestStatus.Pending,
            (await db.AccessRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id)).Status);
        Assert.False(await db.AccessGrants.AsNoTracking().AnyAsync(g => g.AccessRequestId == request.Id));
        Assert.False(await db.AuditLogs.AsNoTracking().AnyAsync(log => log.PatientId == scenario.PatientUser.Id));
    }

    /// <summary>Approval creates the grant, consumes the credential and writes the append-only audit row.</summary>
    [Fact]
    public async Task Approve_PendingRequest_CreatesGrantConsumesCredentialAndWritesAudit()
    {
        var scenario = await _host.CreateVisitScenarioAsync();
        var request = RequireRequest(scenario);
        using var client = await _host.CreateAuthenticatedClientAsync(scenario.PatientUser);

        using var response = await client.PostAsync(
            $"/api/access-requests/{request.Id}/approve",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);

        var grant = await response.Content.ReadFromJsonAsync<AccessGrantResponse>();
        Assert.NotNull(grant);
        Assert.Equal(request.Id, grant.AccessRequestId);
        Assert.Equal(scenario.Appointment.Id, grant.AppointmentId);
        Assert.Equal(scenario.Doctor.Id, grant.DoctorId);
        Assert.Equal(TimeSpan.FromHours(2), grant.ExpiresAtUtc - grant.GrantedAtUtc);

        await using var db = _host.CreateDbContext();

        var storedRequest = await db.AccessRequests.AsNoTracking()
            .SingleAsync(r => r.Id == request.Id);
        Assert.Equal(AccessRequestStatus.Approved, storedRequest.Status);

        var storedGrant = await db.AccessGrants.AsNoTracking()
            .SingleAsync(g => g.AccessRequestId == request.Id);
        Assert.Equal(grant.GrantId, storedGrant.Id);
        Assert.Equal(scenario.PatientUser.Id, storedGrant.PatientId);
        Assert.Equal(scenario.Doctor.Id, storedGrant.DoctorId);

        var credential = await db.VisitAccessCredentials.AsNoTracking()
            .SingleAsync(c => c.AccessRequestId == request.Id);
        Assert.NotNull(credential.ConsumedAtUtc);
        Assert.Null(credential.RevokedAtUtc);

        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(log => log.AccessGrantId == grant.GrantId);
        Assert.Equal(AuditAction.AccessGrantCreated, audit.Action);
        Assert.Equal(AuditEntityType.AccessGrant, audit.EntityType);
        Assert.Equal(scenario.PatientUser.Id, audit.UserId);
        Assert.Equal(scenario.PatientUser.Id, audit.PatientId);
    }

    /// <summary>Rejection revokes the credential, writes the audit row and creates no grant.</summary>
    [Fact]
    public async Task Reject_PendingRequest_RevokesCredentialAndAuditsWithoutGrant()
    {
        var scenario = await _host.CreateVisitScenarioAsync();
        var request = RequireRequest(scenario);
        using var client = await _host.CreateAuthenticatedClientAsync(scenario.PatientUser);

        using var response = await client.PostAsync(
            $"/api/access-requests/{request.Id}/reject",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, response.Content.Headers.ContentLength ?? 0);

        await using var db = _host.CreateDbContext();

        var storedRequest = await db.AccessRequests.AsNoTracking()
            .SingleAsync(r => r.Id == request.Id);
        Assert.Equal(AccessRequestStatus.Rejected, storedRequest.Status);
        Assert.False(await db.AccessGrants.AsNoTracking().AnyAsync(g => g.AccessRequestId == request.Id));

        var credential = await db.VisitAccessCredentials.AsNoTracking()
            .SingleAsync(c => c.AccessRequestId == request.Id);
        Assert.Null(credential.ConsumedAtUtc);
        Assert.NotNull(credential.RevokedAtUtc);

        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(log => log.PatientId == scenario.PatientUser.Id);
        Assert.Equal(AuditAction.AccessRequestRejected, audit.Action);
        Assert.Equal(AuditEntityType.AccessRequest, audit.EntityType);
        Assert.Equal(request.Id.ToString(), audit.EntityId);
        Assert.Equal(scenario.PatientUser.Id, audit.UserId);
        Assert.Null(audit.AccessGrantId);
    }

    /// <summary>A request can be answered once; every later decision is a 409 and changes nothing.</summary>
    [Theory]
    [InlineData("approve", "approve")]
    [InlineData("approve", "reject")]
    [InlineData("reject", "approve")]
    [InlineData("reject", "reject")]
    public async Task Decision_WhenRequestWasAlreadyAnswered_ReturnsConflict(string firstAction, string secondAction)
    {
        var scenario = await _host.CreateVisitScenarioAsync();
        var request = RequireRequest(scenario);
        using var client = await _host.CreateAuthenticatedClientAsync(scenario.PatientUser);

        using var first = await client.PostAsync(
            $"/api/access-requests/{request.Id}/{firstAction}",
            content: null);
        Assert.Equal(
            firstAction == "approve" ? HttpStatusCode.OK : HttpStatusCode.NoContent,
            first.StatusCode);

        using var second = await client.PostAsync(
            $"/api/access-requests/{request.Id}/{secondAction}",
            content: null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);

        await using var db = _host.CreateDbContext();

        var expectedStatus = firstAction == "approve"
            ? AccessRequestStatus.Approved
            : AccessRequestStatus.Rejected;
        Assert.Equal(
            expectedStatus,
            (await db.AccessRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id)).Status);
        Assert.Equal(
            firstAction == "approve" ? 1 : 0,
            await db.AccessGrants.AsNoTracking().CountAsync(g => g.AccessRequestId == request.Id));

        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(log => log.PatientId == scenario.PatientUser.Id);
        Assert.Equal(
            firstAction == "approve" ? AuditAction.AccessGrantCreated : AuditAction.AccessRequestRejected,
            audit.Action);
    }

    private static AccessRequest RequireRequest(VisitScenario scenario) =>
        scenario.Request
        ?? throw new InvalidOperationException("The scenario has no pending access request.");
}

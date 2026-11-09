using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Api.IntegrationTests.Infrastructure;
using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.VisitAccess;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// The end-to-end visit-access path: the doctor starts a checked-in appointment and receives a one-time QR token,
/// then the patient approves the resulting request (FR-ACS-01, FR-ACS-02, FR-ACS-04).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class StartVisitApproveFlowTests
{
    private readonly IntegrationTestHost _host;

    /// <summary>Initializes the test class with the shared host.</summary>
    public StartVisitApproveFlowTests(IntegrationTestHost host)
    {
        _host = host;
    }

    /// <summary>
    /// Start-visit creates the pending request and stores only the hash of the returned token; approving that
    /// request creates the grant and consumes the same credential.
    /// </summary>
    [Fact]
    public async Task StartVisit_ThenApprove_CreatesTheGrantForTheStartedVisit()
    {
        var scenario = await _host.CreateVisitScenarioAsync(includePendingRequest: false);
        using var doctorClient = await _host.CreateAuthenticatedClientAsync(scenario.DoctorUser);

        using var startResponse = await doctorClient.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/start-visit",
            content: null);

        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        Assert.Contains("no-store", startResponse.Headers.CacheControl?.ToString() ?? string.Empty);

        var start = await startResponse.Content.ReadFromJsonAsync<StartVisitResponse>();
        Assert.NotNull(start);
        Assert.Equal(scenario.Appointment.Id, start.AppointmentId);
        Assert.NotEqual(Guid.Empty, start.AccessRequestId);
        Assert.False(string.IsNullOrWhiteSpace(start.QrToken));

        var remainingLifetime = start.ExpiresAtUtc - DateTime.UtcNow;
        Assert.InRange(remainingLifetime, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(125));

        await using (var db = _host.CreateDbContext())
        {
            var storedRequest = await db.AccessRequests.AsNoTracking()
                .SingleAsync(r => r.Id == start.AccessRequestId);
            Assert.Equal(AccessRequestStatus.Pending, storedRequest.Status);
            Assert.Equal(scenario.Appointment.Id, storedRequest.AppointmentId);
            Assert.Equal(scenario.Doctor.Id, storedRequest.DoctorId);
            Assert.Equal(scenario.PatientUser.Id, storedRequest.PatientId);

            var credential = await db.VisitAccessCredentials.AsNoTracking()
                .SingleAsync(c => c.AccessRequestId == start.AccessRequestId);
            Assert.Equal(VisitCredentialType.QrToken, credential.Type);
            Assert.Equal(QrTokenGenerator.Hash(start.QrToken), credential.SecretHash);
            Assert.NotEqual(start.QrToken, credential.SecretHash);
            Assert.Null(credential.ConsumedAtUtc);
            Assert.Null(credential.RevokedAtUtc);

            var audit = await db.AuditLogs.AsNoTracking()
                .SingleAsync(log => log.PatientId == scenario.PatientUser.Id);
            Assert.Equal(AuditAction.AccessRequestCreated, audit.Action);
            Assert.Equal(AuditEntityType.AccessRequest, audit.EntityType);
            Assert.Equal(start.AccessRequestId.ToString(), audit.EntityId);
            Assert.Equal(scenario.DoctorUser.Id, audit.UserId);
        }

        using var patientClient = await _host.CreateAuthenticatedClientAsync(scenario.PatientUser);
        using var approveResponse = await patientClient.PostAsync(
            $"/api/access-requests/{start.AccessRequestId}/approve",
            content: null);

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var grant = await approveResponse.Content.ReadFromJsonAsync<AccessGrantResponse>();
        Assert.NotNull(grant);
        Assert.Equal(start.AccessRequestId, grant.AccessRequestId);
        Assert.Equal(scenario.Appointment.Id, grant.AppointmentId);
        Assert.Equal(scenario.Doctor.Id, grant.DoctorId);
        Assert.Equal(TimeSpan.FromHours(2), grant.ExpiresAtUtc - grant.GrantedAtUtc);

        await using var verificationDb = _host.CreateDbContext();
        var consumed = await verificationDb.VisitAccessCredentials.AsNoTracking()
            .SingleAsync(c => c.AccessRequestId == start.AccessRequestId);
        Assert.NotNull(consumed.ConsumedAtUtc);
        Assert.Equal(QrTokenGenerator.Hash(start.QrToken), consumed.SecretHash);
    }
}

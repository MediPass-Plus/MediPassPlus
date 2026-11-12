using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP matrix for the six-digit visit-access fallback: patient generation, owning-doctor verification, the
/// FR-ACS-05 lockout, the single-use rule and the HMAC-only storage proof (FR-ACS-05, NFR-SEC-04, ADR-0001,
/// ADR-0002, ADR-0007).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AccessCodeEndpointTests
{
    private const string KnownCode = "042915";
    private const string WrongCode = "999999";

    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with the shared API factory.</summary>
    /// <param name="factory">The collection fixture that hosts the API and database.</param>
    public AccessCodeEndpointTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>No bearer token means code generation is never reached.</summary>
    [Fact]
    public async Task GenerateCode_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        using var response = await PostGenerateAsync(client, 42);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Only the Patient role may generate a code.</summary>
    /// <param name="role">The non-patient role under test.</param>
    [Theory]
    [InlineData(DefaultRoles.Doctor)]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task GenerateCode_WithNonPatientRole_ReturnsForbidden(string role)
    {
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await PostGenerateAsync(client, 42);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An unknown appointment id is a 404 with the shared problem-details envelope.</summary>
    [Fact]
    public async Task GenerateCode_ForUnknownAppointment_ReturnsNotFound()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(patient);

        using var response = await PostGenerateAsync(client, 999_999);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
    }

    /// <summary>A patient cannot generate a code for another patient's appointment.</summary>
    [Fact]
    public async Task GenerateCode_ForAnotherPatientsAppointment_ReturnsForbiddenAndWritesNothing()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(otherPatient);

        using var response = await PostGenerateAsync(client, scenario.Appointment.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        Assert.False(await db.VisitAccessCredentials.AsNoTracking()
            .AnyAsync(credential => credential.AccessRequestId == RequireRequest(scenario).Id
                && credential.Type == VisitCredentialType.NumericCode));
    }

    /// <summary>A code exists only once the doctor started the visit and left a pending request.</summary>
    [Fact]
    public async Task GenerateCode_WhenTheVisitHasNoPendingRequest_ReturnsConflict()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(includePendingRequest: false);
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await PostGenerateAsync(client, scenario.Appointment.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        Assert.False(await db.VisitAccessCredentials.AsNoTracking()
            .AnyAsync(credential => credential.Type == VisitCredentialType.NumericCode));
    }

    /// <summary>Once the patient approved access, no new fallback code can be generated.</summary>
    [Fact]
    public async Task GenerateCode_WhenAccessWasAlreadyGranted_ReturnsConflict()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var approval = await client.PostAsync(
            $"/api/access-requests/{RequireRequest(scenario).Id}/approve",
            content: null);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);

        using var response = await PostGenerateAsync(client, scenario.Appointment.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>A code is only available for a visit that is checked in or already in visit.</summary>
    /// <param name="status">The appointment status under test.</param>
    [Theory]
    [InlineData(AppointmentStatus.Booked)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task GenerateCode_WhenTheAppointmentIsNotCheckedIn_ReturnsBadRequest(AppointmentStatus status)
    {
        var scenario = await _factory.CreateVisitScenarioAsync(appointmentStatus: status);
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await PostGenerateAsync(client, scenario.Appointment.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The successful response is the only place the plaintext code appears: it is six digits, the response is not
    /// cacheable, and the database stores only its HMAC (FR-ACS-05, NFR-SEC-04).
    /// </summary>
    [Fact]
    public async Task GenerateCode_PendingRequest_ReturnsSixDigitCodeAndStoresOnlyTheHmac()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await PostGenerateAsync(client, scenario.Appointment.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);

        var code = await response.Content.ReadFromJsonAsync<AccessCodeResponse>();
        Assert.NotNull(code);
        Assert.Equal(scenario.Appointment.Id, code.AppointmentId);
        Assert.Equal(RequireRequest(scenario).Id, code.AccessRequestId);
        Assert.Matches(new Regex("^[0-9]{6}$"), code.Code);
        Assert.InRange(
            code.ExpiresAtUtc - DateTime.UtcNow,
            TimeSpan.FromMinutes(4),
            TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(30)));

        await using var db = _factory.CreateDbContext();

        var stored = await db.VisitAccessCredentials.AsNoTracking()
            .SingleAsync(credential => credential.AccessRequestId == code.AccessRequestId
                && credential.Type == VisitCredentialType.NumericCode);
        Assert.Equal(_factory.HashAccessCode(code.Code), stored.SecretHash);
        Assert.NotEqual(code.Code, stored.SecretHash);
        Assert.DoesNotContain(code.Code, stored.SecretHash, StringComparison.Ordinal);
        Assert.Equal(0, stored.FailedAttempts);
        Assert.Null(stored.ConsumedAtUtc);
        Assert.Null(stored.RevokedAtUtc);

        // The table has no plaintext-code column at all, so there is nowhere else the code could have been stored.
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS "
            + "WHERE TABLE_NAME = 'VisitAccessCredentials';";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        Assert.Contains("SecretHash", columns);
        Assert.DoesNotContain("Code", columns);
        Assert.DoesNotContain("PlaintextCode", columns);
        Assert.DoesNotContain("NumericCode", columns);
    }

    /// <summary>A second generation replaces the live code instead of leaving two usable codes (US-22).</summary>
    [Fact]
    public async Task GenerateCode_SecondCall_ReplacesThePreviousLiveCode()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var firstResponse = await PostGenerateAsync(client, scenario.Appointment.Id);
        var first = await firstResponse.Content.ReadFromJsonAsync<AccessCodeResponse>();
        Assert.NotNull(first);

        await using (var db = _factory.CreateDbContext())
        {
            var firstCredential = await db.VisitAccessCredentials.AsNoTracking()
                .SingleAsync(credential => credential.AccessRequestId == first.AccessRequestId
                    && credential.Type == VisitCredentialType.NumericCode);
            Assert.Null(firstCredential.RevokedAtUtc);
        }

        using var secondResponse = await PostGenerateAsync(client, scenario.Appointment.Id);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var second = await secondResponse.Content.ReadFromJsonAsync<AccessCodeResponse>();
        Assert.NotNull(second);

        await using var verifyDb = _factory.CreateDbContext();
        var credentials = await verifyDb.VisitAccessCredentials.AsNoTracking()
            .Where(credential => credential.AccessRequestId == first.AccessRequestId
                && credential.Type == VisitCredentialType.NumericCode)
            .OrderBy(credential => credential.CreatedAtUtc)
            .ToListAsync();

        Assert.Equal(2, credentials.Count);
        Assert.Single(credentials, credential => credential.RevokedAtUtc is not null);
        var live = Assert.Single(credentials, credential => credential.RevokedAtUtc is null);
        Assert.Equal(_factory.HashAccessCode(second.Code), live.SecretHash);
    }

    /// <summary>The generation budget is a 429 with a Retry-After header.</summary>
    [Fact]
    public async Task GenerateCode_BeyondTheConfiguredLimit_ReturnsTooManyRequests()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var allowed = await PostGenerateAsync(client, scenario.Appointment.Id);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var rejected = await PostGenerateAsync(client, scenario.Appointment.Id);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
    }

    /// <summary>No bearer token means code verification is never reached.</summary>
    [Fact]
    public async Task VerifyCode_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        using var response = await PostVerifyAsync(client, 42, KnownCode);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Only the Doctor role may verify a code.</summary>
    /// <param name="role">The non-doctor role under test.</param>
    [Theory]
    [InlineData(DefaultRoles.Patient)]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task VerifyCode_WithNonDoctorRole_ReturnsForbidden(string role)
    {
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await PostVerifyAsync(client, 42, KnownCode);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An unknown appointment id is a 404.</summary>
    [Fact]
    public async Task VerifyCode_ForUnknownAppointment_ReturnsNotFound()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(accessCode: KnownCode);
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await PostVerifyAsync(client, 999_999, KnownCode);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Another doctor's account cannot verify the code (NFR-SEC-10).</summary>
    [Fact]
    public async Task VerifyCode_ByAnotherDoctor_ReturnsForbiddenAndCreatesNoGrant()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(accessCode: KnownCode);
        var otherDoctor = await CreateOtherDoctorWithProfileAsync(scenario.Clinic.Id);
        using var client = _factory.CreateAuthenticatedClient(otherDoctor);

        using var response = await PostVerifyAsync(client, scenario.Appointment.Id, KnownCode);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        Assert.False(await db.AccessGrants.AsNoTracking()
            .AnyAsync(grant => grant.AppointmentId == scenario.Appointment.Id));
        Assert.Equal(
            AccessRequestStatus.Pending,
            (await db.AccessRequests.AsNoTracking()
                .SingleAsync(request => request.Id == RequireRequest(scenario).Id)).Status);
    }

    /// <summary>A malformed code is a validation failure and does not consume an attempt.</summary>
    [Fact]
    public async Task VerifyCode_WithMalformedCode_ReturnsBadRequestAndDoesNotCountAnAttempt()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(accessCode: KnownCode);
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await PostVerifyAsync(client, scenario.Appointment.Id, "12345");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        var credential = await db.VisitAccessCredentials.AsNoTracking()
            .SingleAsync(candidate => candidate.Type == VisitCredentialType.NumericCode
                && candidate.AccessRequestId == RequireRequest(scenario).Id);
        Assert.Equal(0, credential.FailedAttempts);
        Assert.Null(credential.LockedAtUtc);
    }

    /// <summary>A wrong code records one failed attempt and changes nothing else (FR-ACS-05).</summary>
    [Fact]
    public async Task VerifyCode_WithWrongCode_RecordsTheAttempt()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(accessCode: KnownCode);
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await PostVerifyAsync(client, scenario.Appointment.Id, WrongCode);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);

        await using var db = _factory.CreateDbContext();
        var credential = await db.VisitAccessCredentials.AsNoTracking()
            .SingleAsync(candidate => candidate.Type == VisitCredentialType.NumericCode
                && candidate.AccessRequestId == RequireRequest(scenario).Id);
        Assert.Equal(1, credential.FailedAttempts);
        Assert.Null(credential.LockedAtUtc);
        Assert.Null(credential.ConsumedAtUtc);
        Assert.False(await db.AccessGrants.AsNoTracking()
            .AnyAsync(grant => grant.AppointmentId == scenario.Appointment.Id));
    }

    /// <summary>Five wrong entries lock the code; the correct code can no longer create a grant (FR-ACS-05).</summary>
    [Fact]
    public async Task VerifyCode_AfterFiveWrongEntries_LocksTheCodeInTheDatabase()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(accessCode: KnownCode);
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        for (var attempt = 1; attempt <= VisitAccessCredential.MaxFailedAttempts; attempt++)
        {
            using var response = await PostVerifyAsync(client, scenario.Appointment.Id, WrongCode);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            await using var attemptDb = _factory.CreateDbContext();
            var attempts = await attemptDb.VisitAccessCredentials.AsNoTracking()
                .SingleAsync(candidate => candidate.Type == VisitCredentialType.NumericCode
                    && candidate.AccessRequestId == RequireRequest(scenario).Id);
            Assert.Equal(attempt, attempts.FailedAttempts);
        }

        await using var db = _factory.CreateDbContext();
        var credential = await db.VisitAccessCredentials.AsNoTracking()
            .SingleAsync(candidate => candidate.Type == VisitCredentialType.NumericCode
                && candidate.AccessRequestId == RequireRequest(scenario).Id);
        Assert.Equal(VisitAccessCredential.MaxFailedAttempts, credential.FailedAttempts);
        Assert.NotNull(credential.LockedAtUtc);
        Assert.Null(credential.ConsumedAtUtc);
        Assert.False(await db.AccessGrants.AsNoTracking()
            .AnyAsync(grant => grant.AppointmentId == scenario.Appointment.Id));
        Assert.Equal(
            AccessRequestStatus.Pending,
            (await db.AccessRequests.AsNoTracking()
                .SingleAsync(request => request.Id == RequireRequest(scenario).Id)).Status);
    }

    /// <summary>
    /// Five wrong entries sent at the same time still land on exactly five counted attempts and lock the code; a lost
    /// update would leave the counter below the limit (FR-ACS-05).
    /// </summary>
    [Fact]
    public async Task VerifyCode_WithFiveConcurrentWrongEntries_StillLocksTheCode()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(accessCode: KnownCode);
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, VisitAccessCredential.MaxFailedAttempts)
                .Select(_ => PostVerifyAsync(client, scenario.Appointment.Id, WrongCode)));

        try
        {
            Assert.All(
                responses,
                response => Assert.True(
                    response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict,
                    $"Unexpected status code {response.StatusCode}."));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var db = _factory.CreateDbContext();
        var credential = await db.VisitAccessCredentials.AsNoTracking()
            .SingleAsync(candidate => candidate.Type == VisitCredentialType.NumericCode
                && candidate.AccessRequestId == RequireRequest(scenario).Id);

        Assert.Equal(VisitAccessCredential.MaxFailedAttempts, credential.FailedAttempts);
        Assert.NotNull(credential.LockedAtUtc);
        Assert.Null(credential.ConsumedAtUtc);
        Assert.False(await db.AccessGrants.AsNoTracking()
            .AnyAsync(grant => grant.AppointmentId == scenario.Appointment.Id));
        Assert.Equal(
            AccessRequestStatus.Pending,
            (await db.AccessRequests.AsNoTracking()
                .SingleAsync(request => request.Id == RequireRequest(scenario).Id)).Status);
    }

    /// <summary>A locked code refuses even the correct value and creates no grant.</summary>
    [Fact]
    public async Task VerifyCode_WhenTheCodeIsLocked_ReturnsConflictWithoutCreatingGrant()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(accessCode: KnownCode);

        await using (var seedDb = _factory.CreateDbContext())
        {
            var credential = await seedDb.VisitAccessCredentials
                .SingleAsync(candidate => candidate.Type == VisitCredentialType.NumericCode
                    && candidate.AccessRequestId == RequireRequest(scenario).Id);
            for (var attempt = 0; attempt < VisitAccessCredential.MaxFailedAttempts; attempt++)
            {
                credential.RegisterFailedAttempt(DateTime.UtcNow);
            }

            await seedDb.SaveChangesAsync();
        }

        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);
        using var response = await PostVerifyAsync(client, scenario.Appointment.Id, KnownCode);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);

        await using var db = _factory.CreateDbContext();
        Assert.False(await db.AccessGrants.AsNoTracking()
            .AnyAsync(grant => grant.AppointmentId == scenario.Appointment.Id));
    }

    /// <summary>A code older than five minutes is refused (ADR-0002).</summary>
    [Fact]
    public async Task VerifyCode_WhenTheCodeIsExpired_ReturnsConflict()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(
            accessCode: KnownCode,
            accessCodeCreatedAtUtc: DateTime.UtcNow.AddMinutes(-6));
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await PostVerifyAsync(client, scenario.Appointment.Id, KnownCode);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        Assert.False(await db.AccessGrants.AsNoTracking()
            .AnyAsync(grant => grant.AppointmentId == scenario.Appointment.Id));
    }

    /// <summary>A completed visit can no longer be verified into a grant.</summary>
    [Fact]
    public async Task VerifyCode_WhenTheAppointmentIsCompleted_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(
            appointmentStatus: AppointmentStatus.Completed,
            accessCode: KnownCode);
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await PostVerifyAsync(client, scenario.Appointment.Id, KnownCode);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A matching code is the patient's approval: it creates the two-hour grant, moves the visit to InVisit,
    /// consumes the code, revokes the QR and writes AccessGrantCreated in the same unit of work
    /// (ADR-0011 amendment 2026-11-12, ADR-0004).
    /// </summary>
    [Fact]
    public async Task VerifyCode_WithTheRightCode_CreatesGrantMovesVisitToInVisitAndAudits()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(accessCode: KnownCode);
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await PostVerifyAsync(client, scenario.Appointment.Id, KnownCode);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);

        var grant = await response.Content.ReadFromJsonAsync<AccessGrantResponse>();
        Assert.NotNull(grant);
        Assert.Equal(RequireRequest(scenario).Id, grant.AccessRequestId);
        Assert.Equal(scenario.Appointment.Id, grant.AppointmentId);
        Assert.Equal(scenario.Doctor.Id, grant.DoctorId);
        Assert.Equal(TimeSpan.FromHours(2), grant.ExpiresAtUtc - grant.GrantedAtUtc);

        await using var db = _factory.CreateDbContext();

        var storedGrant = await db.AccessGrants.AsNoTracking()
            .SingleAsync(candidate => candidate.AccessRequestId == RequireRequest(scenario).Id);
        Assert.Equal(grant.GrantId, storedGrant.Id);
        Assert.Equal(scenario.PatientUser.Id, storedGrant.PatientId);
        Assert.Equal(scenario.Doctor.Id, storedGrant.DoctorId);

        var request = await db.AccessRequests.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == RequireRequest(scenario).Id);
        Assert.Equal(AccessRequestStatus.Approved, request.Status);

        var appointment = await db.Appointments.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == scenario.Appointment.Id);
        Assert.Equal(AppointmentStatus.InVisit, appointment.Status);
        Assert.NotNull(appointment.InVisitAtUtc);

        var code = await db.VisitAccessCredentials.AsNoTracking()
            .SingleAsync(candidate => candidate.Type == VisitCredentialType.NumericCode
                && candidate.AccessRequestId == RequireRequest(scenario).Id);
        Assert.NotNull(code.ConsumedAtUtc);
        Assert.Null(code.RevokedAtUtc);

        var qr = await db.VisitAccessCredentials.AsNoTracking()
            .SingleAsync(candidate => candidate.Type == VisitCredentialType.QrToken
                && candidate.AccessRequestId == RequireRequest(scenario).Id);
        Assert.NotNull(qr.RevokedAtUtc);

        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(log => log.AccessGrantId == grant.GrantId);
        Assert.Equal(AuditAction.AccessGrantCreated, audit.Action);
        Assert.Equal(AuditEntityType.AccessGrant, audit.EntityType);
        Assert.Equal(scenario.DoctorUser.Id, audit.UserId);
        Assert.Equal(scenario.PatientUser.Id, audit.PatientId);
        Assert.Equal(grant.GrantId.ToString(), audit.EntityId);
    }

    /// <summary>A consumed code cannot be presented a second time (single use).</summary>
    [Fact]
    public async Task VerifyCode_ReplayedAfterSuccess_ReturnsConflict()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(accessCode: KnownCode);
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var first = await PostVerifyAsync(client, scenario.Appointment.Id, KnownCode);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var replay = await PostVerifyAsync(client, scenario.Appointment.Id, KnownCode);

        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);

        await using var db = _factory.CreateDbContext();
        Assert.Equal(
            1,
            await db.AccessGrants.AsNoTracking()
                .CountAsync(grant => grant.AppointmentId == scenario.Appointment.Id));
    }

    /// <summary>The verification budget is a 429 with a Retry-After header.</summary>
    [Fact]
    public async Task VerifyCode_BeyondTheConfiguredLimit_ReturnsTooManyRequests()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(accessCode: KnownCode);
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var allowed = await PostVerifyAsync(client, scenario.Appointment.Id, WrongCode);
            Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
        }

        using var rejected = await PostVerifyAsync(client, scenario.Appointment.Id, WrongCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
    }

    private static Task<HttpResponseMessage> PostGenerateAsync(HttpClient client, int appointmentId) =>
        client.PostAsync($"/api/appointments/{appointmentId}/access-code", content: null);

    private static Task<HttpResponseMessage> PostVerifyAsync(
        HttpClient client,
        int appointmentId,
        string code) =>
        client.PostAsJsonAsync(
            $"/api/appointments/{appointmentId}/verify-code",
            new VerifyAccessCodeRequest(code));

    private static AccessRequest RequireRequest(VisitScenario scenario) =>
        scenario.Request
        ?? throw new InvalidOperationException("The scenario has no pending access request.");

    private async Task<TestUser> CreateOtherDoctorWithProfileAsync(int clinicId)
    {
        var user = await _factory.CreateUserAsync(DefaultRoles.Doctor);

        await using var db = _factory.CreateDbContext();
        db.Doctors.Add(new Doctor
        {
            UserId = user.Id,
            Specialty = "Cardiology",
            ClinicId = clinicId
        });
        await db.SaveChangesAsync();

        return user;
    }
}

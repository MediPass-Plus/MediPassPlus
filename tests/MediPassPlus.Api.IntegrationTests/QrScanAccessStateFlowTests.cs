using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using MediPassPlus.Infrastructure.VisitAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// The MP-97 path over real HTTP: the doctor receives a rendered QR, the patient scans it as text or as a camera
/// image, approves or rejects the request it proves, and both parties read the derived access state. The negative
/// matrix covers authentication, the wrong-patient boundary, unusable tokens, closed visits, unsupported uploads and
/// the shared per-patient rate limit (NFR-SEC-10, FR-ACS-02, FR-ACS-06, FR-ACS-07, ADR-0001, ADR-0007).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class QrScanAccessStateFlowTests
{
    private const int PermitLimit = 5;

    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with the shared API factory.</summary>
    /// <param name="factory">The collection fixture that hosts the API and database.</param>
    public QrScanAccessStateFlowTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Start-visit returns the same token as a scannable PNG data URI.</summary>
    [Fact]
    public async Task StartVisit_ReturnsAQRCodeDataUriThatDecodesToThePlaintextToken()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(includePendingRequest: false);
        using var doctorClient = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await doctorClient.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/start-visit",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var start = await response.Content.ReadFromJsonAsync<StartVisitResponse>();
        Assert.NotNull(start);
        Assert.StartsWith("data:image/png;base64,", start.QrCodeDataUri, StringComparison.Ordinal);

        var png = Convert.FromBase64String(
            start.QrCodeDataUri["data:image/png;base64,".Length..]);
        Assert.Equal(start.QrToken, new QrImageDecoder().Decode(png));
    }

    /// <summary>Scanning shows the doctor and clinic, does not consume the token, and approving it creates the grant
    /// that the access-state read model then reports as active.</summary>
    [Fact]
    public async Task ScanThenApproveByToken_ThenAccessStateReportsTheActiveGrant()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        using var patientClient = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var scanResponse = await patientClient.PostAsJsonAsync(
            "/api/access/scan",
            new QrTokenRequest(scenario.QrToken!));

        Assert.Equal(HttpStatusCode.OK, scanResponse.StatusCode);
        Assert.Contains("no-store", scanResponse.Headers.CacheControl?.ToString() ?? string.Empty);

        var scan = await scanResponse.Content.ReadFromJsonAsync<AccessRequestScanResponse>();
        Assert.NotNull(scan);
        Assert.Equal(scenario.Request!.Id, scan.AccessRequestId);
        Assert.Equal(scenario.Appointment.Id, scan.AppointmentId);
        Assert.Equal("Doctor Integration Test", scan.DoctorName);
        Assert.Equal("General Medicine", scan.DoctorSpecialty);
        Assert.Equal(scenario.Clinic.Name, scan.ClinicName);
        Assert.Equal(scenario.Clinic.Address, scan.ClinicAddress);

        await using (var db = _factory.CreateDbContext())
        {
            var stored = await db.VisitAccessCredentials.AsNoTracking()
                .SingleAsync(credential => credential.Id == scenario.Credential!.Id);
            Assert.Null(stored.ConsumedAtUtc);
            Assert.Null(stored.RevokedAtUtc);
        }

        using var approveResponse = await patientClient.PostAsJsonAsync(
            "/api/access-requests/token/approve",
            new QrTokenRequest(scenario.QrToken!));

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var grant = await approveResponse.Content.ReadFromJsonAsync<AccessGrantResponse>();
        Assert.NotNull(grant);
        Assert.Equal(scenario.Request.Id, grant.AccessRequestId);
        Assert.Equal(scenario.Appointment.Id, grant.AppointmentId);
        Assert.Equal(TimeSpan.FromHours(2), grant.ExpiresAtUtc - grant.GrantedAtUtc);

        using var doctorClient = _factory.CreateAuthenticatedClient(scenario.DoctorUser);
        using var stateResponse = await doctorClient.GetAsync(
            $"/api/appointments/{scenario.Appointment.Id}/access-state");

        Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        var state = await stateResponse.Content.ReadFromJsonAsync<AccessStateResponse>();
        Assert.NotNull(state);
        Assert.Equal(scenario.Appointment.Id, state.AppointmentId);
        Assert.Equal(scenario.Request.Id, state.AccessRequestId);
        Assert.Equal(nameof(AccessRequestStatus.Approved), state.RequestStatus);
        Assert.Equal(grant.GrantId, state.GrantId);
        Assert.Equal(nameof(AccessGrantStatus.Active), state.GrantStatus);
        Assert.Null(state.EndedAtUtc);
        Assert.InRange(state.RemainingSeconds, 7190, 7200);
    }

    /// <summary>Approving the same token twice creates one grant; the replay is refused and consumes nothing.</summary>
    [Fact]
    public async Task ApproveByToken_WhenReplayedAfterSuccess_Returns410AndCreatesExactlyOneGrant()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        using var patientClient = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var first = await patientClient.PostAsJsonAsync(
            "/api/access-requests/token/approve",
            new QrTokenRequest(scenario.QrToken!));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var replay = await patientClient.PostAsJsonAsync(
            "/api/access-requests/token/approve",
            new QrTokenRequest(scenario.QrToken!));
        Assert.Equal(HttpStatusCode.Gone, replay.StatusCode);

        await using var db = _factory.CreateDbContext();
        Assert.Equal(
            1,
            await db.AccessGrants.AsNoTracking()
                .CountAsync(grant => grant.AppointmentId == scenario.Appointment.Id));

        var credential = await db.VisitAccessCredentials.AsNoTracking()
            .SingleAsync(item => item.Id == scenario.Credential!.Id);
        Assert.NotNull(credential.ConsumedAtUtc);
    }

    /// <summary>The owning patient sees the same active grant after a refresh that the doctor sees.</summary>
    [Fact]
    public async Task AccessState_ForTheOwningPatient_ReturnsTheActiveGrant()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        using var patientClient = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var approve = await patientClient.PostAsJsonAsync(
            "/api/access-requests/token/approve",
            new QrTokenRequest(scenario.QrToken!));
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        using var response = await patientClient.GetAsync(
            $"/api/appointments/{scenario.Appointment.Id}/access-state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Content.ReadFromJsonAsync<AccessStateResponse>();
        Assert.NotNull(state);
        Assert.Equal(scenario.Request!.Id, state.AccessRequestId);
        Assert.Equal(nameof(AccessRequestStatus.Approved), state.RequestStatus);
        Assert.Equal(nameof(AccessGrantStatus.Active), state.GrantStatus);
        Assert.Null(state.EndedAtUtc);
        Assert.InRange(state.RemainingSeconds, 7190, 7200);
    }

    /// <summary>A camera frame is decoded by the server and resolves the same request as a text scan.</summary>
    [Fact]
    public async Task ScanImage_WithTheRenderedQrPng_ResolvesTheRequest()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        var dataUri = new QrCodeRenderer().RenderDataUri(scenario.QrToken!);
        var png = Convert.FromBase64String(dataUri["data:image/png;base64,".Length..]);

        using var patientClient = _factory.CreateAuthenticatedClient(scenario.PatientUser);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "image", "qr.png");

        using var response = await patientClient.PostAsync(
            "/api/access-requests/scan-image",
            content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scan = await response.Content.ReadFromJsonAsync<AccessRequestScanResponse>();
        Assert.NotNull(scan);
        Assert.Equal(scenario.Request!.Id, scan.AccessRequestId);
        Assert.Equal(scenario.Appointment.Id, scan.AppointmentId);
    }

    /// <summary>Another patient cannot use the token, and the refusal does not reveal that it exists.</summary>
    [Fact]
    public async Task Scan_WhenAnotherPatientPresentsTheToken_Returns403()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(otherPatient);

        using var response = await client.PostAsJsonAsync(
            "/api/access/scan",
            new QrTokenRequest(scenario.QrToken!));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);

        await using var db = _factory.CreateDbContext();
        Assert.False(await db.AccessGrants.AsNoTracking()
            .AnyAsync(grant => grant.AppointmentId == scenario.Appointment.Id));
    }

    /// <summary>An unknown token is refused by the scan route with the same 403 as a foreign token.</summary>
    [Fact]
    public async Task Scan_WhenTheTokenIsUnknown_Returns403()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(patient);

        using var response = await client.PostAsJsonAsync(
            "/api/access/scan",
            new QrTokenRequest("an-unknown-token"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.Status);
    }

    /// <summary>An expired token is gone, not forbidden (410).</summary>
    [Fact]
    public async Task Scan_WhenTheTokenExpired_Returns410()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(
            qrTokenCreatedAtUtc: DateTime.UtcNow.AddMinutes(-5));
        using var patientClient = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await patientClient.PostAsJsonAsync(
            "/api/access/scan",
            new QrTokenRequest(scenario.QrToken!));

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(StatusCodes.Status410Gone, problem!.Status);

        await using var db = _factory.CreateDbContext();
        Assert.False(await db.AccessGrants.AsNoTracking()
            .AnyAsync(grant => grant.AppointmentId == scenario.Appointment.Id));
    }

    /// <summary>A token for a visit that already ended is gone (410).</summary>
    [Fact]
    public async Task Scan_WhenTheVisitCompleted_Returns410()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(AppointmentStatus.Completed);
        using var patientClient = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await patientClient.PostAsJsonAsync(
            "/api/access/scan",
            new QrTokenRequest(scenario.QrToken!));

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    /// <summary>The token decision path refuses a closed visit with 410.</summary>
    [Fact]
    public async Task ApproveByToken_WhenTheVisitCompleted_Returns410()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(AppointmentStatus.Completed);
        using var patientClient = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await patientClient.PostAsJsonAsync(
            "/api/access-requests/token/approve",
            new QrTokenRequest(scenario.QrToken!));

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        Assert.False(await db.AccessGrants.AsNoTracking()
            .AnyAsync(grant => grant.AppointmentId == scenario.Appointment.Id));
    }

    /// <summary>The request-id path keeps its 409 for a closed visit instead of the token path's 410.</summary>
    [Fact]
    public async Task ApproveByRequestId_WhenTheVisitCompleted_Returns409()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(AppointmentStatus.Completed);
        using var patientClient = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await patientClient.PostAsync(
            $"/api/access-requests/{scenario.Request!.Id}/approve",
            content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(StatusCodes.Status409Conflict, problem!.Status);
    }

    /// <summary>An unknown token answers 403, exactly like another patient's token.</summary>
    [Fact]
    public async Task ApproveByToken_WhenTheTokenIsUnknown_Returns403()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(patient);

        using var response = await client.PostAsJsonAsync(
            "/api/access-requests/token/approve",
            new QrTokenRequest("an-unknown-token"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Rejecting by token creates no grant and makes the token unusable.</summary>
    [Fact]
    public async Task RejectByToken_ThenTheTokenCannotBeScannedAgain()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        using var patientClient = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var rejectResponse = await patientClient.PostAsJsonAsync(
            "/api/access-requests/token/reject",
            new QrTokenRequest(scenario.QrToken!));

        Assert.Equal(HttpStatusCode.NoContent, rejectResponse.StatusCode);

        using var scanResponse = await patientClient.PostAsJsonAsync(
            "/api/access/scan",
            new QrTokenRequest(scenario.QrToken!));

        Assert.Equal(HttpStatusCode.Gone, scanResponse.StatusCode);

        await using var db = _factory.CreateDbContext();
        Assert.False(await db.AccessGrants.AsNoTracking()
            .AnyAsync(grant => grant.AppointmentId == scenario.Appointment.Id));
        Assert.Equal(
            AccessRequestStatus.Rejected,
            (await db.AccessRequests.AsNoTracking()
                .SingleAsync(request => request.Id == scenario.Request!.Id)).Status);
    }

    /// <summary>A non-image part is refused with 415 before any decode is attempted.</summary>
    [Fact]
    public async Task ScanImage_WhenTheUploadIsNotAnImage_Returns415()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        using var patientClient = _factory.CreateAuthenticatedClient(scenario.PatientUser);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3, 4]);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "image", "notes.txt");

        using var response = await patientClient.PostAsync(
            "/api/access-requests/scan-image",
            content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, problem!.Status);
    }

    /// <summary>Bytes that are not a readable image answer 400, not a server error.</summary>
    [Fact]
    public async Task ScanImage_WhenTheImageHasNoQrCode_Returns400()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        using var patientClient = _factory.CreateAuthenticatedClient(scenario.PatientUser);
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3, 4]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "image", "camera.jpg");

        using var response = await patientClient.PostAsync(
            "/api/access-requests/scan-image",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Scanning requires the patient's token.</summary>
    [Fact]
    public async Task Scan_WithoutAToken_Returns401()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/access/scan",
            new QrTokenRequest("a-token"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Reading the access state requires the patient's or the doctor's token.</summary>
    [Fact]
    public async Task AccessState_WithoutAToken_Returns401()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/appointments/1/access-state");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Another patient cannot read the appointment's access state.</summary>
    [Fact]
    public async Task AccessState_ForAnotherPatient_Returns403()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(otherPatient);

        using var response = await client.GetAsync(
            $"/api/appointments/{scenario.Appointment.Id}/access-state");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Another doctor cannot read the appointment's access state.</summary>
    [Fact]
    public async Task AccessState_ForAnotherDoctor_Returns403()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        var otherDoctor = await _factory.CreateUserAsync(DefaultRoles.Doctor);
        using var client = _factory.CreateAuthenticatedClient(otherDoctor);

        using var response = await client.GetAsync(
            $"/api/appointments/{scenario.Appointment.Id}/access-state");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An appointment that does not exist answers 404.</summary>
    [Fact]
    public async Task AccessState_ForAnUnknownAppointment_Returns404()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        using var doctorClient = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await doctorClient.GetAsync(
            $"/api/appointments/{int.MaxValue}/access-state");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Scanning shares the per-patient QrApprove window with the decision endpoints.</summary>
    [Fact]
    public async Task SixthScanInTheWindow_Returns429WithRetryAfter()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(patient);

        for (var attempt = 1; attempt <= PermitLimit; attempt++)
        {
            using var allowed = await client.PostAsJsonAsync(
                "/api/access/scan",
                new QrTokenRequest($"unknown-token-{attempt}"));
            Assert.Equal(HttpStatusCode.Forbidden, allowed.StatusCode);
        }

        using var rejected = await client.PostAsJsonAsync(
            "/api/access/scan",
            new QrTokenRequest("unknown-token-6"));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter?.Delta);

        var problem = await rejected.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status429TooManyRequests, problem.Status);
        Assert.Equal("Too many access-request attempts.", problem.Title);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// MP-55: wrong-doctor and foreign-patient cases for the visit-access and status endpoints, plus the 401 cases
/// for a missing or invalid token (NFR-SEC-10, NFR-PRI-01). Every refusal must leave state untouched, use the
/// shared problem-details shape and carry no medical text or patient data.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class MedicalVisitAccessMatrixTests : IClassFixture<MediPassApiFactory>
{
    private readonly MediPassApiFactory _factory;

    public MedicalVisitAccessMatrixTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    // ---------- 401: missing or invalid token ----------

    [Theory]
    [InlineData("GET", "/api/appointments/1/history")]
    [InlineData("POST", "/api/appointments/1/diagnoses")]
    [InlineData("POST", "/api/appointments/1/start-visit")]
    [InlineData("GET", "/api/me/history")]
    [InlineData("GET", "/api/appointments/1/access-state")]
    public async Task InvalidToken_ReturnsUnauthorizedWithoutMedicalText(string method, string path)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Synthetic", body, StringComparison.Ordinal);
    }

    // ---------- Wrong doctor ----------

    [Fact]
    public async Task StartVisit_ByAnotherDoctor_ReturnsForbiddenAndChangesNothing()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        var otherDoctor = await CreateDoctorWithProfileAsync(scenario.Clinic.Id);
        using var client = _factory.CreateAuthenticatedClient(otherDoctor);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/start-visit", content: null);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, scenario.PatientUser.Email);
        await AssertRequestStillPendingAsync(scenario);
    }

    [Fact]
    public async Task StartVisit_ForAnUnknownAppointment_ReturnsNotFoundProblem()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await client.PostAsync("/api/appointments/999999/start-visit", content: null);

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Complete_ByAnotherDoctor_ReturnsForbiddenAndKeepsTheVisitOpen()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(AppointmentStatus.InVisit);
        var otherDoctor = await CreateDoctorWithProfileAsync(scenario.Clinic.Id);
        using var client = _factory.CreateAuthenticatedClient(otherDoctor);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/complete", content: null);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, scenario.PatientUser.Email);

        await using var db = _factory.CreateDbContext();
        var appointment = await db.Appointments.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == scenario.Appointment.Id);
        Assert.Equal(AppointmentStatus.InVisit, appointment.Status);
    }

    [Fact]
    public async Task Complete_ForAnUnknownAppointment_ReturnsNotFoundProblem()
    {
        var scenario = await _factory.CreateVisitScenarioAsync(AppointmentStatus.InVisit);
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await client.PostAsync("/api/appointments/999999/complete", content: null);

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AccessState_ByAnotherDoctor_ReturnsForbidden()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        var otherDoctor = await CreateDoctorWithProfileAsync(scenario.Clinic.Id);
        using var client = _factory.CreateAuthenticatedClient(otherDoctor);

        using var response = await client.GetAsync($"/api/appointments/{scenario.Appointment.Id}/access-state");

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, scenario.PatientUser.Email);
    }

    [Fact]
    public async Task AccessState_ForAnUnknownAppointment_ReturnsNotFoundProblem()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await client.GetAsync("/api/appointments/999999/access-state");

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    // ---------- Foreign patient ----------

    [Fact]
    public async Task AccessState_ByAnotherPatient_ReturnsForbidden()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(otherPatient);

        using var response = await client.GetAsync($"/api/appointments/{scenario.Appointment.Id}/access-state");

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, scenario.PatientUser.Email);
    }

    [Fact]
    public async Task GenerateCode_ByAnotherPatient_ReturnsForbiddenProblem()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(otherPatient);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/access-code", content: null);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, scenario.PatientUser.Email);
    }

    [Fact]
    public async Task ApproveRequest_ByAnotherPatient_ReturnsForbiddenAndCreatesNoGrant()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(otherPatient);

        using var response = await client.PostAsync(
            $"/api/access-requests/{scenario.Request!.Id}/approve", content: null);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, scenario.PatientUser.Email);
        await AssertRequestStillPendingAsync(scenario);

        await using var db = _factory.CreateDbContext();
        Assert.False(await db.AccessGrants.AsNoTracking()
            .AnyAsync(grant => grant.AppointmentId == scenario.Appointment.Id));
    }

    [Fact]
    public async Task RejectRequest_ByAnotherPatient_ReturnsForbiddenAndLeavesTheRequestPending()
    {
        var scenario = await _factory.CreateVisitScenarioAsync();
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(otherPatient);

        using var response = await client.PostAsync(
            $"/api/access-requests/{scenario.Request!.Id}/reject", content: null);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, scenario.PatientUser.Email);
        await AssertRequestStillPendingAsync(scenario);
    }

    [Fact]
    public async Task ApproveRequest_ForAnUnknownRequest_ReturnsNotFoundProblem()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(patient);

        using var response = await client.PostAsync(
            $"/api/access-requests/{Guid.NewGuid()}/approve", content: null);

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ApproveByToken_WithAnUnknownToken_ReturnsForbiddenProblem()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(patient);

        using var response = await client.PostAsJsonAsync(
            "/api/access-requests/token/approve",
            new { token = new string('a', 43) });

        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
    }

    // ---------- Helpers ----------

    private async Task<TestUser> CreateDoctorWithProfileAsync(int clinicId)
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

    private async Task AssertRequestStillPendingAsync(VisitScenario scenario)
    {
        await using var db = _factory.CreateDbContext();
        var request = await db.AccessRequests.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == scenario.Request!.Id);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);
    }

    /// <summary>
    /// Asserts the status code, the problem-details shape and that the body leaks neither seeded medical text nor
    /// the patient's e-mail address.
    /// </summary>
    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string? patientEmail = null)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Contains("json", response.Content.Headers.ContentType?.MediaType ?? string.Empty);

        var body = await response.Content.ReadAsStringAsync();
        foreach (var marker in new[]
        {
            MediPassApiFactory.HistoryDiagnosisPrefix,
            MediPassApiFactory.HistoryMedicinePrefix,
            MediPassApiFactory.HistoryDosagePrefix,
            MediPassApiFactory.HistoryNotesPrefix
        })
        {
            Assert.DoesNotContain(marker, body, StringComparison.Ordinal);
        }

        if (patientEmail is not null)
        {
            Assert.DoesNotContain(patientEmail, body, StringComparison.OrdinalIgnoreCase);
        }

        var problem = JsonSerializer.Deserialize<ProblemDetails>(
            body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(problem);
        Assert.Equal((int)expected, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.False(string.IsNullOrWhiteSpace(problem.Type));
    }
}
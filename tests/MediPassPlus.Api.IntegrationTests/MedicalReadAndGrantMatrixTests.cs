using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// MP-55: grant lifecycle and foreign-owner cases for the doctor history read and the patient grant end
/// (NFR-SEC-10, NFR-PRI-01). Refusals must be ProblemDetails and carry no medical text.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class MedicalReadAndGrantMatrixTests : IClassFixture<MediPassApiFactory>
{
    private const string GenericForbiddenTitle = "You cannot access this record.";

    private readonly MediPassApiFactory _factory;

    public MedicalReadAndGrantMatrixTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    // ---------- Doctor history: grant lifecycle ----------

    [Fact]
    public async Task History_WithoutGrant_ReturnsForbiddenProblemWithoutMedicalText()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync(includeActiveGrant: false);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.GetAsync($"/api/appointments/{scenario.Visit.Appointment.Id}/history");

        await AssertForbiddenProblemAsync(response);
    }

    [Fact]
    public async Task History_WhenTheGrantHasExpired_ReturnsForbiddenProblemWithoutMedicalText()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync(
            grantedAtUtc: DateTime.UtcNow.AddHours(-3));
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.GetAsync($"/api/appointments/{scenario.Visit.Appointment.Id}/history");

        await AssertForbiddenProblemAsync(response);
    }

    [Fact]
    public async Task History_WhenThePatientEndedTheGrant_ReturnsForbiddenProblemWithoutMedicalText()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync();
        Assert.NotNull(scenario.Grant);

        using (var patient = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser))
        using (var end = await patient.PostAsync($"/api/access-grants/{scenario.Grant!.Id}/end", content: null))
        {
            Assert.Equal(HttpStatusCode.OK, end.StatusCode);
        }

        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.GetAsync($"/api/appointments/{scenario.Visit.Appointment.Id}/history");

        await AssertForbiddenProblemAsync(response);
    }

    [Fact]
    public async Task History_ForAnotherDoctorsVisit_ReturnsForbiddenProblemWithoutMedicalText()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync();
        var otherDoctor = await _factory.CreateUserAsync(DefaultRoles.Doctor);

        await using (var db = _factory.CreateDbContext())
        {
            db.Doctors.Add(new Doctor
            {
                UserId = otherDoctor.Id,
                Specialty = "Dermatology",
                ClinicId = scenario.Visit.Clinic.Id
            });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateAuthenticatedClient(otherDoctor);

        using var response = await client.GetAsync($"/api/appointments/{scenario.Visit.Appointment.Id}/history");

        await AssertForbiddenProblemAsync(response);
    }

    [Fact]
    public async Task History_ForAnUnknownAppointment_IsRefusedLikeAForeignOne()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.GetAsync("/api/appointments/999999/history");

        await AssertForbiddenProblemAsync(response);
    }

    // ---------- Patient grant end: foreign owner ----------

    [Fact]
    public async Task EndGrant_ByAnotherPatient_ReturnsForbiddenAndLeavesTheGrantLive()
    {
        var scenario = await _factory.CreateRecordHistoryScenarioAsync();
        Assert.NotNull(scenario.Grant);
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(otherPatient);

        using var response = await client.PostAsync($"/api/access-grants/{scenario.Grant!.Id}/end", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemWithoutMedicalTextAsync(response);

        // The grant is still live: the owning doctor can still read.
        using var doctor = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);
        using var read = await doctor.GetAsync($"/api/appointments/{scenario.Visit.Appointment.Id}/history");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task EndGrant_ForAnUnknownGrant_ReturnsNotFoundProblem()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(patient);

        using var response = await client.PostAsync($"/api/access-grants/{Guid.NewGuid()}/end", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblemWithoutMedicalTextAsync(response);
    }

    // ---------- Helpers ----------

    private static async Task AssertForbiddenProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = await AssertProblemWithoutMedicalTextAsync(response);
        Assert.Equal(GenericForbiddenTitle, problem.Title);
    }

private static async Task<ProblemDetails> AssertProblemWithoutMedicalTextAsync(HttpResponseMessage response)
{
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

    var problem = System.Text.Json.JsonSerializer.Deserialize<ProblemDetails>(
        body,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    Assert.NotNull(problem);
    Assert.NotNull(problem.Status);
    Assert.False(string.IsNullOrWhiteSpace(problem.Title));
    Assert.False(string.IsNullOrWhiteSpace(problem.Type));
    return problem;
}
}


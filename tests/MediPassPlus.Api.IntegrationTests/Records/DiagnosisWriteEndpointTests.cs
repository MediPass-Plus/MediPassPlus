using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Application.Records;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests.Records;

/// <summary>
/// The negative HTTP matrix for the doctor write path: authentication, the doctor role, the active grant, the grant
/// lifecycle and the doctor scope on both record endpoints (FR-REC-03, FR-REC-04, FR-ACS-06, FR-ACS-07, NFR-SEC-10).
/// Every refusal must leave the record untouched, and an unknown diagnosis must be refused exactly like one outside
/// the grant's scope, so a caller can never learn that another patient's record exists (NFR-PRI-01).
/// </summary>
/// <remarks>
/// Every refusal here is produced by the authorization middleware or the access-grant filter and asserts nothing about
/// encryption; the rows these tests seed carry placeholder ciphertext. The harness hosts the real protector that
/// MP-50 registered, over its own ephemeral key ring, so a request that passes the grant check is never a 500.
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
public sealed class DiagnosisWriteEndpointTests : IClassFixture<MediPassApiFactory>
{
    private const string GenericForbiddenTitle = "You cannot access this record.";

    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with its own API factory and database.</summary>
    /// <param name="factory">The class fixture that hosts this class's API and database.</param>
    public DiagnosisWriteEndpointTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateDiagnosis_WithoutToken_ReturnsUnauthorized()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/diagnoses",
            NewDiagnosisRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario);
    }

    [Theory]
    [InlineData(DefaultRoles.Patient)]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task CreateDiagnosis_WithNonDoctorRole_ReturnsForbidden(string role)
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/diagnoses",
            NewDiagnosisRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario);
    }

    [Fact]
    public async Task CreateDiagnosis_WithoutGrant_ReturnsForbiddenAndStoresNothing()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync(includeActiveGrant: false);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/diagnoses",
            NewDiagnosisRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(GenericForbiddenTitle, problem.Title);

        await AssertNothingWasStoredAsync(scenario);
    }

    [Fact]
    public async Task CreateDiagnosis_WhenTheGrantHasExpired_ReturnsForbidden()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync(
            grantedAtUtc: DateTime.UtcNow.AddHours(-3));
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/diagnoses",
            NewDiagnosisRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario);
    }

    [Fact]
    public async Task CreateDiagnosis_WhenThePatientEndedTheGrant_ReturnsForbidden()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        await EndGrantAsync(scenario);

        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/diagnoses",
            NewDiagnosisRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario);
    }

    [Fact]
    public async Task CreateDiagnosis_WhenAnotherDoctorOwnsTheVisit_ReturnsForbidden()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        var otherDoctor = await CreateOtherDoctorAsync(scenario.Visit.Clinic.Id);
        using var client = _factory.CreateAuthenticatedClient(otherDoctor);

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/diagnoses",
            NewDiagnosisRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario);
    }

    [Fact]
    public async Task AddPrescription_WithoutToken_ReturnsUnauthorized()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/diagnoses/{scenario.Diagnosis.Id}/prescriptions",
            NewPrescriptionRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario);
    }

    [Theory]
    [InlineData(DefaultRoles.Patient)]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task AddPrescription_WithNonDoctorRole_ReturnsForbidden(string role)
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await client.PostAsJsonAsync(
            $"/api/diagnoses/{scenario.Diagnosis.Id}/prescriptions",
            NewPrescriptionRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario);
    }

    [Fact]
    public async Task AddPrescription_WithoutGrant_ReturnsForbiddenAndStoresNothing()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync(includeActiveGrant: false);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/diagnoses/{scenario.Diagnosis.Id}/prescriptions",
            NewPrescriptionRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(GenericForbiddenTitle, problem.Title);

        await AssertNothingWasStoredAsync(scenario);
    }

    [Fact]
    public async Task AddPrescription_WhenTheGrantHasExpired_ReturnsForbidden()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync(
            grantedAtUtc: DateTime.UtcNow.AddHours(-3));
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/diagnoses/{scenario.Diagnosis.Id}/prescriptions",
            NewPrescriptionRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario);
    }

    [Fact]
    public async Task AddPrescription_WhenThePatientEndedTheGrant_ReturnsForbidden()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        await EndGrantAsync(scenario);

        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/diagnoses/{scenario.Diagnosis.Id}/prescriptions",
            NewPrescriptionRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario);
    }

    [Fact]
    public async Task AddPrescription_WhenAnotherDoctorOwnsTheVisit_ReturnsForbidden()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        var otherDoctor = await CreateOtherDoctorAsync(scenario.Visit.Clinic.Id);
        using var client = _factory.CreateAuthenticatedClient(otherDoctor);

        using var response = await client.PostAsJsonAsync(
            $"/api/diagnoses/{scenario.Diagnosis.Id}/prescriptions",
            NewPrescriptionRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario);
    }

    [Fact]
    public async Task AddPrescription_ForAnUnknownDiagnosis_IsRefusedLikeOneOutsideTheGrantScope()
    {
        var outsideScope = await _factory.CreateDiagnosisWriteScenarioAsync(includeActiveGrant: false);
        using var outsideClient = _factory.CreateAuthenticatedClient(outsideScope.Visit.DoctorUser);
        using var outsideResponse = await outsideClient.PostAsJsonAsync(
            $"/api/diagnoses/{outsideScope.Diagnosis.Id}/prescriptions",
            NewPrescriptionRequest());

        // The caller holds an active grant for their own visit, but the diagnosis does not exist at all.
        var unknown = await _factory.CreateDiagnosisWriteScenarioAsync();
        using var unknownClient = _factory.CreateAuthenticatedClient(unknown.Visit.DoctorUser);
        using var unknownResponse = await unknownClient.PostAsJsonAsync(
            $"/api/diagnoses/{Guid.NewGuid()}/prescriptions",
            NewPrescriptionRequest());

        Assert.Equal(HttpStatusCode.Forbidden, outsideResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unknownResponse.StatusCode);

        var outside = await outsideResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        var missing = await unknownResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(outside);
        Assert.NotNull(missing);
        Assert.Equal(outside.Type, missing.Type);
        Assert.Equal(outside.Title, missing.Title);
        Assert.Equal(outside.Detail, missing.Detail);

        await AssertNothingWasStoredAsync(unknown);
    }

    private static CreateDiagnosisRequest NewDiagnosisRequest() =>
        new() { Description = "Synthetic diagnosis text used by an access test." };

    private static CreatePrescriptionRequest NewPrescriptionRequest() =>
        new()
        {
            MedicineName = "Synthetic medicine",
            Dosage = "1 tablet daily",
            Notes = "Synthetic note used by an access test."
        };

    /// <summary>
    /// Ends the seeded grant through the patient's own endpoint, so the refusal is proven against a grant the API
    /// actually ended (FR-ACS-09).
    /// </summary>
    /// <param name="scenario">The seeded record-write graph.</param>
    private async Task EndGrantAsync(DiagnosisWriteScenario scenario)
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

    /// <summary>
    /// Asserts that a refused request appended no diagnosis and no prescription to the seeded record.
    /// </summary>
    /// <param name="scenario">The seeded record-write graph.</param>
    private async Task AssertNothingWasStoredAsync(DiagnosisWriteScenario scenario)
    {
        await using var db = _factory.CreateDbContext();

        Assert.Equal(
            1,
            await db.Diagnoses.CountAsync(candidate => candidate.AppointmentId == scenario.Visit.Appointment.Id));
        Assert.False(
            await db.Prescriptions.AnyAsync(candidate => candidate.DiagnosisId == scenario.Diagnosis.Id));
    }
}

using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Application.Records;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests.Records;

/// <summary>
/// MP-49: clinical text never travels back on a refused write. A rejection may name the field and its limit, but it
/// must not echo the submitted text, and a refused write must store no row at all (FR-PRV-04, NFR-SEC-05, ADR-0007).
/// </summary>
/// <remarks>
/// The harness hosts the real protector over an ephemeral Data Protection ring, so a leak would be a real leak. The
/// marker below is unique to this class: finding it in a response body proves the submitted text reached the caller.
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
public sealed class DiagnosisTextLeakTests : IClassFixture<MediPassApiFactory>
{
    private const string Marker = "MP49-clinical-marker";

    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with its own API factory and database.</summary>
    /// <param name="factory">The class fixture that hosts this class's API and database.</param>
    public DiagnosisTextLeakTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateDiagnosis_WhenTheDescriptionIsTooLong_DoesNotEchoTheText()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        var description = Marker + new string(
            'x',
            CreateDiagnosisRequestValidator.DescriptionMaxLength);

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/diagnoses",
            new CreateDiagnosisRequest { Description = description });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Marker, body, StringComparison.Ordinal);

        // A rejected write stores nothing, so no ciphertext of the marker can exist either.
        await using var db = _factory.CreateDbContext();
        Assert.Equal(
            1,
            await db.Diagnoses.AsNoTracking()
                .CountAsync(row => row.AppointmentId == scenario.Visit.Appointment.Id));
    }

    [Fact]
    public async Task AddPrescription_WhenTheMedicineNameIsTooLong_DoesNotEchoTheText()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        var medicineName = Marker + new string(
            'x',
            CreatePrescriptionRequestValidator.MedicineNameMaxLength);

        using var response = await client.PostAsJsonAsync(
            $"/api/diagnoses/{scenario.Diagnosis.Id}/prescriptions",
            new CreatePrescriptionRequest
            {
                MedicineName = medicineName,
                Dosage = "Synthetic dosage"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Marker, body, StringComparison.Ordinal);

        await using var db = _factory.CreateDbContext();
        Assert.False(await db.Prescriptions.AsNoTracking()
            .AnyAsync(row => row.DiagnosisId == scenario.Diagnosis.Id));
    }

    [Fact]
    public async Task CreateDiagnosis_WithoutAGrant_DoesNotEchoTheText()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync(includeActiveGrant: false);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Visit.Appointment.Id}/diagnoses",
            new CreateDiagnosisRequest { Description = $"{Marker} refused write" });

        // The refusal is produced before validation runs, so the body is the generic problem document.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Marker, body, StringComparison.Ordinal);

        await using var db = _factory.CreateDbContext();
        Assert.Equal(
            1,
            await db.Diagnoses.AsNoTracking()
                .CountAsync(row => row.AppointmentId == scenario.Visit.Appointment.Id));
    }
}

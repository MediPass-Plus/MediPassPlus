using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP coverage for the registration change MP-28 makes: a patient account owns exactly one permanent medical
/// record (FR-REC-01, ADR-0005), a doctor or staff account owns none, and a refused registration leaves neither a
/// user nor a record behind. The real endpoint, Identity stores and SQL Server database are exercised; nothing is
/// stubbed.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AuthRegistrationTests
{
    private const string Password = "Integration1!";
    private const string RegisterPath = "/api/Auth/register";

    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with the shared API factory.</summary>
    /// <param name="factory">The collection fixture that hosts the API and database.</param>
    public AuthRegistrationTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("Patient")]
    [InlineData("patient")]
    [InlineData("PATIENT")]
    public async Task Registering_a_patient_creates_exactly_one_medical_record(string role)
    {
        var email = NewEmail("patient");

        var response = await RegisterAsync("Patient Integration Test", email, role);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);

        // Identity resolves the role through its normalized name, so every casing above really assigns Patient.
        var record = await db.MedicalRecords.SingleAsync(row => row.PatientId == user.Id);
        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.True(record.CreatedAtUtc <= DateTime.UtcNow);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Staff")]
    public async Task Registering_a_non_patient_creates_no_medical_record(string role)
    {
        var email = NewEmail(role.ToLowerInvariant());

        var response = await RegisterAsync($"{role} Integration Test", email, role);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);

        Assert.False(await db.MedicalRecords.AnyAsync(row => row.PatientId == user.Id));
    }

    [Fact]
    public async Task A_duplicate_registration_is_refused_and_creates_no_second_record()
    {
        var email = NewEmail("duplicate");

        Assert.Equal(
            HttpStatusCode.OK,
            (await RegisterAsync("Duplicate Patient", email, "Patient")).StatusCode);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await RegisterAsync("Duplicate Patient", email, "Patient")).StatusCode);

        await using var db = _factory.CreateDbContext();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);

        // The refused attempt never reaches the record insert, so the unique PatientId index is untouched.
        Assert.Equal(1, await db.MedicalRecords.CountAsync(row => row.PatientId == user.Id));
    }

    [Fact]
    public async Task A_refused_registration_creates_no_user_and_no_orphan_record()
    {
        var email = NewEmail("weak");

        await using var db = _factory.CreateDbContext();
        var recordsBefore = await db.MedicalRecords.CountAsync();

        var response = await RegisterAsync("Weak Password", email, "Patient", password: "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await db.Users.AnyAsync(candidate => candidate.Email == email));

        // The whole registration rolled back: no partial account and no record that no user owns.
        Assert.Equal(recordsBefore, await db.MedicalRecords.CountAsync());
    }

    private async Task<HttpResponseMessage> RegisterAsync(
        string fullName,
        string email,
        string role,
        string password = Password)
    {
        using var client = _factory.CreateClient();
        using var body = new StringContent(
            $$"""{"fullName":"{{fullName}}","email":"{{email}}","role":"{{role}}","password":"{{password}}"}""",
            Encoding.UTF8,
            "application/json");

        return await client.PostAsync(RegisterPath, body);
    }

    private static string NewEmail(string prefix) =>
        $"{prefix}.{Guid.NewGuid():N}@integration.medipass.test";
}

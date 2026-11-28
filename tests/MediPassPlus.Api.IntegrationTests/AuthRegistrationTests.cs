using System.Net;
using System.Text;
using System.Text.Json;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP coverage for the registration change MP-28 makes: a patient account owns exactly one permanent medical
/// record (FR-REC-01, ADR-0005), a doctor or staff account owns none, and a refused registration leaves neither a
/// user nor a record behind. Since MP-21 only patients self-register, so the non-patient accounts are created by an
/// administrator (FR-ACC-03). The real endpoints, Identity stores and SQL Server database are exercised; nothing is
/// stubbed.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AuthRegistrationTests : IClassFixture<MediPassApiFactory>
{
    private const string Password = "Integration1!";
    private const string RegisterPath = "/api/Auth/register";

    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with its own API factory and database.</summary>
    /// <param name="factory">The class fixture that hosts this class's API and database.</param>
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
    [InlineData(DefaultRoles.Doctor)]
    [InlineData(DefaultRoles.Staff)]
    public async Task An_admin_created_non_patient_owns_no_medical_record(string role)
    {
        var admin = await _factory.CreateUserAsync(DefaultRoles.Admin);
        using var client = _factory.CreateAuthenticatedClient(admin);
        var clinicId = await CreateClinicAsync(client);
        var email = NewEmail(role.ToLowerInvariant());

        using var response = await CreateAccountAsync(client, role, email, clinicId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

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

        using var duplicate = await RegisterAsync("Duplicate Patient", email, "Patient");

        // The Identity failure goes through the shared ProblemDetails envelope, not a raw string or an array
        // (ADR-0010), and it never repeats the submitted password.
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);

        var body = await duplicate.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.Equal((int)HttpStatusCode.BadRequest, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            "The account could not be created.",
            problem.RootElement.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("detail").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain(Password, body, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash", body, StringComparison.OrdinalIgnoreCase);

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

    private static async Task<int> CreateClinicAsync(HttpClient client)
    {
        using var body = new StringContent(
            """{"name":"Registration Clinic","address":"1 Integration Way","phone":"555-0100"}""",
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync("/api/admin/clinics", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetInt32();
    }

    private static async Task<HttpResponseMessage> CreateAccountAsync(
        HttpClient client, string role, string email, int clinicId)
    {
        var json = role == DefaultRoles.Doctor
            ? $$"""{"fullName":"Doctor Integration Test","email":"{{email}}","password":"{{Password}}","specialty":"Cardiology","clinicId":{{clinicId}}}"""
            : $$"""{"fullName":"Staff Integration Test","email":"{{email}}","password":"{{Password}}","clinicId":{{clinicId}}}""";
        var path = role == DefaultRoles.Doctor ? "/api/admin/doctors" : "/api/admin/staff";

        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync(path, body);
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

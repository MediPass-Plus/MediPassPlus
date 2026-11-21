using System.Net;
using System.Net.Http.Json;
using System.Text;
using MediPassPlus.Application.Records;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Records;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests.Records;

/// <summary>
/// The HTTP contract of a report download: a patient on their own report and a doctor with an active grant can stream
/// the file, and every refusal answers without leaking existence or exposing the stored path/name (FR-REC-08,
/// FR-PRV-01, FR-PRV-02, FR-PRV-05, NFR-PRI-01, NFR-SEC-09, NFR-SEC-10).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class ReportDownloadEndpointTests : IClassFixture<MediPassApiFactory>
{
    private static readonly byte[] PdfBytes =
        Encoding.ASCII.GetBytes("%PDF-1.7\nsynthetic report used by a download test\n%%EOF\n");

    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03];

    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with its own API factory and database.</summary>
    /// <param name="factory">The class fixture that hosts this class's API and database.</param>
    public ReportDownloadEndpointTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Download_WhenThePatientOwnsTheReport_ReturnsTheFileWithSafeHeadersAndAudits()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        var report = await SeedReportAsync(scenario, PdfBytes, ReportFileValidator.PdfContentType);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);

        using var response = await client.GetAsync($"/api/reports/{report.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ReportFileValidator.PdfContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PdfBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Equal("report.pdf", disposition.FileName ?? disposition.FileNameStar);
        Assert.DoesNotContain(report.StoredFileName, await GetRawDispositionAsync(response));

        await using var db = _factory.CreateDbContext();
        var audit = await db.AuditLogs.SingleAsync(candidate =>
            candidate.EntityId == report.Id.ToString() && candidate.Action == AuditAction.ReportDownloaded);
        Assert.Equal(scenario.Visit.PatientUser.Id, audit.UserId);
        Assert.Equal(scenario.Visit.PatientUser.Id, audit.PatientId);
        Assert.Equal(AuditEntityType.Report, audit.EntityType);
        Assert.Null(audit.AccessGrantId);
    }

    [Fact]
    public async Task Download_WhenADoctorHoldsAnActiveGrant_ReturnsTheFileAndNamesTheGrantInAudit()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        Assert.NotNull(scenario.Grant);
        var report = await SeedReportAsync(scenario, PngBytes, ReportFileValidator.PngContentType);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.GetAsync($"/api/reports/{report.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ReportFileValidator.PngContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PngBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("report.png", response.Content.Headers.ContentDisposition?.FileName
            ?? response.Content.Headers.ContentDisposition?.FileNameStar);

        await using var db = _factory.CreateDbContext();
        var audit = await db.AuditLogs.SingleAsync(candidate =>
            candidate.EntityId == report.Id.ToString() && candidate.Action == AuditAction.ReportDownloaded);
        Assert.Equal(scenario.Visit.DoctorUser.Id, audit.UserId);
        Assert.Equal(scenario.Visit.PatientUser.Id, audit.PatientId);
        Assert.Equal(scenario.Grant!.Id, audit.AccessGrantId);
    }

    [Fact]
    public async Task Download_WithoutToken_ReturnsUnauthorized()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        var report = await SeedReportAsync(scenario, PdfBytes, ReportFileValidator.PdfContentType);
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/api/reports/{report.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNoDownloadAuditAsync(report.Id);
    }

    [Theory]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task Download_WithClinicRole_ReturnsForbidden(string role)
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        var report = await SeedReportAsync(scenario, PdfBytes, ReportFileValidator.PdfContentType);
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await client.GetAsync($"/api/reports/{report.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNoDownloadAuditAsync(report.Id);
    }

    [Fact]
    public async Task Download_WhenTheReportDoesNotExist_ReturnsNotFound()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);

        using var response = await client.GetAsync($"/api/reports/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_WhenAnotherPatientOwnsTheReport_ReturnsNotFound()
    {
        var owner = await _factory.CreateDiagnosisWriteScenarioAsync();
        var other = await _factory.CreateDiagnosisWriteScenarioAsync();
        var report = await SeedReportAsync(owner, PdfBytes, ReportFileValidator.PdfContentType);
        using var client = _factory.CreateAuthenticatedClient(other.Visit.PatientUser);

        using var response = await client.GetAsync($"/api/reports/{report.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoDownloadAuditAsync(report.Id);
    }

    [Fact]
    public async Task Download_WhenTheDoctorHoldsNoGrant_ReturnsNotFound()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync(includeActiveGrant: false);
        var report = await SeedReportAsync(scenario, PdfBytes, ReportFileValidator.PdfContentType);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.GetAsync($"/api/reports/{report.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoDownloadAuditAsync(report.Id);
    }

    [Fact]
    public async Task Download_WhenTheGrantHasExpired_ReturnsNotFound()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync(
            grantedAtUtc: DateTime.UtcNow.AddHours(-3));
        var report = await SeedReportAsync(scenario, PdfBytes, ReportFileValidator.PdfContentType);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.GetAsync($"/api/reports/{report.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoDownloadAuditAsync(report.Id);
    }

    [Fact]
    public async Task Download_WhenThePatientEndedTheGrant_ReturnsNotFound()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        await EndGrantAsync(scenario);
        var report = await SeedReportAsync(scenario, PdfBytes, ReportFileValidator.PdfContentType);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);

        using var response = await client.GetAsync($"/api/reports/{report.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoDownloadAuditAsync(report.Id);
    }

    [Fact]
    public async Task Download_WhenRefused_AnswersExactlyLikeAMissingReport()
    {
        var owner = await _factory.CreateDiagnosisWriteScenarioAsync();
        var other = await _factory.CreateDiagnosisWriteScenarioAsync();
        var report = await SeedReportAsync(owner, PdfBytes, ReportFileValidator.PdfContentType);
        using var foreignClient = _factory.CreateAuthenticatedClient(other.Visit.PatientUser);
        using var foreignResponse = await foreignClient.GetAsync($"/api/reports/{report.Id}");

        using var missingClient = _factory.CreateAuthenticatedClient(other.Visit.PatientUser);
        using var missingResponse = await missingClient.GetAsync($"/api/reports/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        var foreign = await foreignResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        var missing = await missingResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(foreign);
        Assert.NotNull(missing);
        Assert.Equal(missing.Type, foreign.Type);
        Assert.Equal(missing.Title, foreign.Title);
        Assert.Equal(missing.Detail, foreign.Detail);
    }

    private async Task<Report> SeedReportAsync(
        DiagnosisWriteScenario scenario,
        byte[] content,
        string contentType)
    {
        var extension = ReportFileValidator.ExtensionFor(contentType);
        var storedFileName = Guid.NewGuid().ToString("N") + extension;
        var path = Path.Combine(_factory.ReportStorageRootPath, storedFileName);
        Directory.CreateDirectory(_factory.ReportStorageRootPath);
        await File.WriteAllBytesAsync(path, content);

        await using var db = _factory.CreateDbContext();
        var report = Report.Create(
            scenario.Record.Id,
            scenario.Visit.PatientUser.Id,
            storedFileName,
            contentType,
            content.LongLength,
            DateTime.UtcNow);
        db.Reports.Add(report);
        await db.SaveChangesAsync();

        return report;
    }

    private async Task AssertNoDownloadAuditAsync(Guid reportId)
    {
        await using var db = _factory.CreateDbContext();
        Assert.False(await db.AuditLogs.AnyAsync(candidate =>
            candidate.EntityId == reportId.ToString() && candidate.Action == AuditAction.ReportDownloaded));
    }

    private static Task<string> GetRawDispositionAsync(HttpResponseMessage response)
    {
        response.Content.Headers.TryGetValues("Content-Disposition", out var values);
        return Task.FromResult(values is null ? string.Empty : string.Join(";", values));
    }

    private async Task EndGrantAsync(DiagnosisWriteScenario scenario)
    {
        Assert.NotNull(scenario.Grant);

        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);
        using var response = await client.PostAsync(
            $"/api/access-grants/{scenario.Grant!.Id}/end",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

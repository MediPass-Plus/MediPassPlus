using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MediPassPlus.Api.Records;
using MediPassPlus.Application.Records;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests.Records;

/// <summary>
/// The HTTP contract of a report upload: a patient on their own record, a doctor with an active grant, and every
/// refusal — no token, the wrong role, a foreign record, no grant, an expired grant, an ended grant, a file that is too
/// large or of the wrong type (FR-REC-05, FR-REC-06, FR-ACS-06, FR-ACS-07, NFR-PRI-01, NFR-SEC-09, NFR-SEC-10).
/// </summary>
/// <remarks>
/// A successful upload is proven twice: the report row and its audit entry in the database, and the file under the
/// factory's private storage root with a random name the response never reveals. Every refusal is also checked against
/// the storage root, so a rejected request leaves no bytes behind.
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
public sealed class ReportUploadEndpointTests : IClassFixture<MediPassApiFactory>
{
    private const string StoredNamePattern = "^[0-9a-f]{32}\\.(pdf|jpg|png)$";

    private static readonly byte[] PdfBytes =
        Encoding.ASCII.GetBytes("%PDF-1.7\nsynthetic report used by an upload test\n%%EOF\n");

    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04];

    private static readonly byte[] JpegBytes =
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with its own API factory and database.</summary>
    /// <param name="factory">The class fixture that hosts this class's API and database.</param>
    public ReportUploadEndpointTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Upload_WhenAPatientUploadsToTheirOwnRecord_ReturnsCreatedAndStoresPrivately()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        var before = FilesOnDisk();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);
        using var content = Upload(PdfBytes, ReportFileValidator.PdfContentType);

        using var response = await client.PostAsync(
            $"/api/records/{scenario.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var metadata = await response.Content.ReadFromJsonAsync<ReportMetadataResponse>();
        Assert.NotNull(metadata);
        Assert.Equal(ReportFileValidator.PdfContentType, metadata.ContentType);
        Assert.Equal(PdfBytes.Length, metadata.SizeBytes);
        Assert.Equal(scenario.Visit.PatientUser.Id, metadata.UploadedByUserId);

        var created = Assert.Single(FilesOnDisk().Except(before));
        Assert.Matches(StoredNamePattern, Path.GetFileName(created));
        Assert.Equal(PdfBytes, await File.ReadAllBytesAsync(created));

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Path.GetFileNameWithoutExtension(created), body);

        await using var db = _factory.CreateDbContext();
        var row = await db.Reports.SingleAsync(candidate => candidate.Id == metadata.Id);
        Assert.Equal(scenario.Record.Id, row.MedicalRecordId);
        Assert.Equal(scenario.Visit.PatientUser.Id, row.UploadedByUserId);
        Assert.Equal(Path.GetFileName(created), row.StoredFileName);

        var audit = await db.AuditLogs.SingleAsync(candidate => candidate.EntityId == metadata.Id.ToString());
        Assert.Equal(AuditAction.ReportUploaded, audit.Action);
        Assert.Equal(AuditEntityType.Report, audit.EntityType);
        Assert.Equal(scenario.Visit.PatientUser.Id, audit.UserId);
        Assert.Equal(scenario.Visit.PatientUser.Id, audit.PatientId);
        Assert.Null(audit.AccessGrantId);
    }

    [Fact]
    public async Task Upload_WhenADoctorHoldsAnActiveGrant_ReturnsCreatedAndNamesTheGrantInTheAuditRow()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        Assert.NotNull(scenario.Grant);
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);
        using var content = Upload(PngBytes, ReportFileValidator.PngContentType);

        using var response = await client.PostAsync(
            $"/api/records/{scenario.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var metadata = await response.Content.ReadFromJsonAsync<ReportMetadataResponse>();
        Assert.NotNull(metadata);

        await using var db = _factory.CreateDbContext();
        var audit = await db.AuditLogs.SingleAsync(candidate => candidate.EntityId == metadata.Id.ToString());
        Assert.Equal(scenario.Visit.DoctorUser.Id, audit.UserId);
        Assert.Equal(scenario.Visit.PatientUser.Id, audit.PatientId);
        Assert.Equal(scenario.Grant!.Id, audit.AccessGrantId);
    }

    [Fact]
    public async Task Upload_WithoutToken_ReturnsUnauthorizedAndStoresNothing()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        var before = FilesOnDisk();
        using var client = _factory.CreateClient();
        using var content = Upload(PdfBytes, ReportFileValidator.PdfContentType);

        using var response = await client.PostAsync(
            $"/api/records/{scenario.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario.Record.Id, before);
    }

    [Theory]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task Upload_WithClinicRole_ReturnsForbiddenAndStoresNothing(string role)
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        var user = await _factory.CreateUserAsync(role);
        var before = FilesOnDisk();
        using var client = _factory.CreateAuthenticatedClient(user);
        using var content = Upload(PdfBytes, ReportFileValidator.PdfContentType);

        using var response = await client.PostAsync(
            $"/api/records/{scenario.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario.Record.Id, before);
    }

    [Fact]
    public async Task Upload_WhenTheRecordDoesNotExist_ReturnsNotFound()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        var before = FilesOnDisk();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);
        using var content = Upload(PdfBytes, ReportFileValidator.PdfContentType);

        using var response = await client.PostAsync(
            $"/api/records/{Guid.NewGuid()}/reports",
            content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingWasStoredAsync(recordId: null, before);
    }

    [Fact]
    public async Task Upload_WhenAnotherPatientOwnsTheRecord_ReturnsNotFound()
    {
        var owner = await _factory.CreateDiagnosisWriteScenarioAsync();
        var other = await _factory.CreateDiagnosisWriteScenarioAsync();
        var before = FilesOnDisk();
        using var client = _factory.CreateAuthenticatedClient(other.Visit.PatientUser);
        using var content = Upload(PdfBytes, ReportFileValidator.PdfContentType);

        using var response = await client.PostAsync(
            $"/api/records/{owner.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingWasStoredAsync(owner.Record.Id, before);
    }

    [Fact]
    public async Task Upload_WhenTheDoctorHoldsNoGrant_ReturnsNotFound()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync(includeActiveGrant: false);
        var before = FilesOnDisk();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);
        using var content = Upload(PdfBytes, ReportFileValidator.PdfContentType);

        using var response = await client.PostAsync(
            $"/api/records/{scenario.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario.Record.Id, before);
    }

    [Fact]
    public async Task Upload_WhenTheGrantHasExpired_ReturnsNotFound()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync(
            grantedAtUtc: DateTime.UtcNow.AddHours(-3));
        var before = FilesOnDisk();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);
        using var content = Upload(PdfBytes, ReportFileValidator.PdfContentType);

        using var response = await client.PostAsync(
            $"/api/records/{scenario.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario.Record.Id, before);
    }

    [Fact]
    public async Task Upload_WhenThePatientEndedTheGrant_ReturnsNotFound()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        await EndGrantAsync(scenario);
        var before = FilesOnDisk();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.DoctorUser);
        using var content = Upload(PdfBytes, ReportFileValidator.PdfContentType);

        using var response = await client.PostAsync(
            $"/api/records/{scenario.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario.Record.Id, before);
    }

    [Fact]
    public async Task Upload_WhenTheDoctorHoldsAGrantForAnotherPatient_ReturnsNotFound()
    {
        var visiting = await _factory.CreateDiagnosisWriteScenarioAsync();
        var unrelated = await _factory.CreateDiagnosisWriteScenarioAsync();
        var before = FilesOnDisk();
        using var client = _factory.CreateAuthenticatedClient(visiting.Visit.DoctorUser);
        using var content = Upload(PdfBytes, ReportFileValidator.PdfContentType);

        // The doctor's live grant covers their own patient, never the owner of this record.
        using var response = await client.PostAsync(
            $"/api/records/{unrelated.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingWasStoredAsync(unrelated.Record.Id, before);
    }

    [Fact]
    public async Task Upload_WithoutAFilePart_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);
        using var content = new MultipartFormDataContent
        {
            { new StringContent("report"), "description" }
        };

        using var response = await client.PostAsync(
            $"/api/records/{scenario.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("file", problem.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upload_WhenTheFileIsEmpty_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        var before = FilesOnDisk();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);
        using var content = Upload([], ReportFileValidator.PdfContentType);

        using var response = await client.PostAsync(
            $"/api/records/{scenario.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario.Record.Id, before);
    }

    [Fact]
    public async Task Upload_WhenTheFileIsLargerThanTenMegabytes_ReturnsPayloadTooLarge()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        var before = FilesOnDisk();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);
        using var content = Upload(
            PdfBytesOfSize(ReportFileValidator.MaxSizeBytes + 1),
            ReportFileValidator.PdfContentType);

        using var response = await client.PostAsync(
            $"/api/records/{scenario.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        await AssertPayloadTooLargeProblemAsync(response);
        await AssertNothingWasStoredAsync(scenario.Record.Id, before);
    }

    [Fact]
    public async Task Upload_WhenTheBodyExceedsTheRequestLimit_ReturnsPayloadTooLarge()
    {
        // The multipart body is larger than the controller's request window (10 MB + 64 KB), so the refusal must
        // happen before the form reader can turn it into a generic model-binding 400 (FR-REC-05, NFR-SEC-09).
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        var before = FilesOnDisk();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);
        using var content = Upload(
            PdfBytesOfSize(11 * 1024 * 1024),
            ReportFileValidator.PdfContentType);

        using var response = await client.PostAsync(
            $"/api/records/{scenario.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        await AssertPayloadTooLargeProblemAsync(response);
        await AssertNothingWasStoredAsync(scenario.Record.Id, before);
    }

    [Fact]
    public async Task Upload_WhenTheFileIsExactlyTenMegabytes_ReturnsCreated()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);
        using var content = Upload(
            PdfBytesOfSize(ReportFileValidator.MaxSizeBytes),
            ReportFileValidator.PdfContentType);

        using var response = await client.PostAsync(
            $"/api/records/{scenario.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var metadata = await response.Content.ReadFromJsonAsync<ReportMetadataResponse>();
        Assert.NotNull(metadata);
        Assert.Equal(ReportFileValidator.MaxSizeBytes, metadata.SizeBytes);
    }

    [Fact]
    public async Task Upload_WhenTheFileIsAJpeg_ReturnsCreated()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);
        using var content = Upload(JpegBytes, ReportFileValidator.JpegContentType);

        using var response = await client.PostAsync(
            $"/api/records/{scenario.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var metadata = await response.Content.ReadFromJsonAsync<ReportMetadataResponse>();
        Assert.NotNull(metadata);
        Assert.Equal(ReportFileValidator.JpegContentType, metadata.ContentType);
    }

    [Fact]
    public async Task Upload_WithATraversalFileName_StoresUnderARandomNameInsideTheRoot()
    {
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        var before = FilesOnDisk();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);
        using var content = Upload(PdfBytes, ReportFileValidator.PdfContentType, "../../outside.pdf");

        using var response = await client.PostAsync(
            $"/api/records/{scenario.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = Assert.Single(FilesOnDisk().Except(before));
        Assert.Matches(StoredNamePattern, Path.GetFileName(created));
        Assert.Equal(
            Path.GetFullPath(_factory.ReportStorageRootPath),
            Path.GetFullPath(Path.GetDirectoryName(created)!));
        Assert.False(File.Exists(Path.Combine(_factory.ReportStorageRootPath, "..", "outside.pdf")));
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    [InlineData("application/pdf")]
    public async Task Upload_WhenTheTypeIsNotAccepted_ReturnsUnsupportedMediaType(string contentType)
    {
        // "application/pdf" here declares a PDF but sends PNG bytes, so the refusal is the content mismatch.
        var scenario = await _factory.CreateDiagnosisWriteScenarioAsync();
        var before = FilesOnDisk();
        using var client = _factory.CreateAuthenticatedClient(scenario.Visit.PatientUser);
        using var content = Upload(PngBytes, contentType);

        using var response = await client.PostAsync(
            $"/api/records/{scenario.Record.Id}/reports",
            content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        await AssertNothingWasStoredAsync(scenario.Record.Id, before);
    }

    [Fact]
    public async Task Upload_WhenTheRecordIsRefused_AnswersExactlyLikeAMissingRecord()
    {
        var owner = await _factory.CreateDiagnosisWriteScenarioAsync();
        var other = await _factory.CreateDiagnosisWriteScenarioAsync();
        using var foreignClient = _factory.CreateAuthenticatedClient(other.Visit.PatientUser);
        using var foreignContent = Upload(PdfBytes, ReportFileValidator.PdfContentType);
        using var foreignResponse = await foreignClient.PostAsync(
            $"/api/records/{owner.Record.Id}/reports",
            foreignContent);

        using var missingClient = _factory.CreateAuthenticatedClient(other.Visit.PatientUser);
        using var missingContent = Upload(PdfBytes, ReportFileValidator.PdfContentType);
        using var missingResponse = await missingClient.PostAsync(
            $"/api/records/{Guid.NewGuid()}/reports",
            missingContent);

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

    private static MultipartFormDataContent Upload(
        byte[] bytes,
        string contentType,
        string fileName = "upload.bin")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        return new MultipartFormDataContent
        {
            { file, "file", fileName }
        };
    }

    /// <summary>
    /// Builds a PDF of exactly <paramref name="sizeBytes"/> bytes with a valid leading signature, so a boundary test
    /// exercises the size rule rather than the content check.
    /// </summary>
    /// <param name="sizeBytes">The total file length, including the PDF signature.</param>
    private static byte[] PdfBytesOfSize(long sizeBytes)
    {
        var bytes = new byte[checked((int)sizeBytes)];
        PdfBytes.AsSpan(0, Math.Min(PdfBytes.Length, bytes.Length)).CopyTo(bytes);

        return bytes;
    }

    private string[] FilesOnDisk() =>
        Directory.Exists(_factory.ReportStorageRootPath)
            ? Directory.GetFiles(_factory.ReportStorageRootPath)
            : [];

    /// <summary>Asserts the shared <c>413</c> envelope, so both size refusals answer the same contract.</summary>
    /// <param name="response">The oversized upload response.</param>
    private static async Task AssertPayloadTooLargeProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, problem.Status);
        Assert.Equal(ReportUploadRequestLimits.PayloadTooLargeType, problem.Type);
        Assert.Contains("10 MB", problem.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// Asserts that a refused request stored no file and appended no report row for the affected record.
    /// </summary>
    /// <param name="recordId">The record the request targeted, or null when the identifier matches no record.</param>
    /// <param name="filesBefore">The storage snapshot taken before the request.</param>
    private async Task AssertNothingWasStoredAsync(Guid? recordId, string[] filesBefore)
    {
        Assert.Equal(filesBefore.Order(), FilesOnDisk().Order());

        await using var db = _factory.CreateDbContext();

        if (recordId is null)
        {
            return;
        }

        Assert.False(await db.Reports.AnyAsync(candidate => candidate.MedicalRecordId == recordId));
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

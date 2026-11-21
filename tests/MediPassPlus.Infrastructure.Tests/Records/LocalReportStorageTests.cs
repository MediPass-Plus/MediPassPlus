using MediPassPlus.Application.Records;
using MediPassPlus.Infrastructure.Records;
using Microsoft.Extensions.Options;

namespace MediPassPlus.Infrastructure.Tests.Records;

/// <summary>
/// The private file store: random names, an extension taken from the validated type, the 10 MB cap counted while
/// copying, and a delete that cannot escape its root (FR-REC-06, FR-PRV-05, NFR-SEC-09, ADR-0015 decision 6).
/// </summary>
public sealed class LocalReportStorageTests
{
    private static readonly byte[] PdfBytes = "%PDF-1.7 sample report"u8.ToArray();

    [Fact]
    public async Task Save_Writes_The_Bytes_Under_A_Random_Name()
    {
        using var root = new TemporaryStorageRoot();
        var storage = CreateStorage(root.StoragePath);

        var stored = await storage.SaveAsync(new MemoryStream(PdfBytes), ReportFileValidator.PdfContentType);

        Assert.Matches("^[0-9a-f]{32}\\.pdf$", stored.StoredFileName);
        Assert.Equal(PdfBytes.Length, stored.SizeBytes);

        var path = Path.Combine(root.StoragePath, stored.StoredFileName);

        Assert.True(File.Exists(path));
        Assert.Equal(PdfBytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Save_Gives_Every_Upload_Its_Own_Name()
    {
        using var root = new TemporaryStorageRoot();
        var storage = CreateStorage(root.StoragePath);

        var first = await storage.SaveAsync(new MemoryStream(PdfBytes), ReportFileValidator.PdfContentType);
        var second = await storage.SaveAsync(new MemoryStream(PdfBytes), ReportFileValidator.PdfContentType);

        Assert.NotEqual(first.StoredFileName, second.StoredFileName);
        Assert.Equal(2, Directory.GetFiles(root.StoragePath).Length);
    }

    [Fact]
    public async Task Save_Creates_The_Root_Directory_On_First_Upload()
    {
        using var root = new TemporaryStorageRoot();
        var storage = CreateStorage(root.StoragePath);

        Assert.False(Directory.Exists(root.StoragePath));

        await storage.SaveAsync(new MemoryStream(PdfBytes), ReportFileValidator.PdfContentType);

        Assert.True(Directory.Exists(root.StoragePath));
    }

    [Theory]
    [InlineData(ReportFileValidator.PdfContentType, ".pdf")]
    [InlineData(ReportFileValidator.JpegContentType, ".jpg")]
    [InlineData(ReportFileValidator.PngContentType, ".png")]
    public async Task Save_Takes_The_Extension_From_The_Validated_Media_Type(
        string contentType,
        string extension)
    {
        using var root = new TemporaryStorageRoot();
        var storage = CreateStorage(root.StoragePath);

        var stored = await storage.SaveAsync(new MemoryStream(PdfBytes), contentType);

        Assert.EndsWith(extension, stored.StoredFileName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_Refuses_An_Unsupported_Media_Type_And_Writes_Nothing()
    {
        using var root = new TemporaryStorageRoot();
        var storage = CreateStorage(root.StoragePath);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => storage.SaveAsync(new MemoryStream(PdfBytes), "text/plain"));

        Assert.Empty(FilesIn(root.StoragePath));
    }

    [Fact]
    public async Task Save_Refuses_A_File_One_Byte_Past_The_Limit_And_Leaves_Nothing()
    {
        using var root = new TemporaryStorageRoot();
        var storage = CreateStorage(root.StoragePath);
        var oversized = new MemoryStream(new byte[ReportFileValidator.MaxSizeBytes + 1]);

        var exception = await Assert.ThrowsAsync<ReportFileSizeException>(
            () => storage.SaveAsync(oversized, ReportFileValidator.PdfContentType));

        Assert.Equal(ReportFileValidationFailure.TooLarge, exception.Failure);

        // No final file and no temporary file: a refused upload is invisible on disk.
        Assert.Empty(FilesIn(root.StoragePath));
    }

    [Fact]
    public async Task Save_Accepts_A_File_Exactly_At_The_Limit()
    {
        using var root = new TemporaryStorageRoot();
        var storage = CreateStorage(root.StoragePath);
        var atLimit = new MemoryStream(new byte[ReportFileValidator.MaxSizeBytes]);

        var stored = await storage.SaveAsync(atLimit, ReportFileValidator.PdfContentType);

        Assert.Equal(ReportFileValidator.MaxSizeBytes, stored.SizeBytes);
        Assert.Single(Directory.GetFiles(root.StoragePath));
    }

    [Fact]
    public async Task Save_Refuses_An_Empty_Stream_And_Leaves_Nothing()
    {
        using var root = new TemporaryStorageRoot();
        var storage = CreateStorage(root.StoragePath);

        var exception = await Assert.ThrowsAsync<ReportFileSizeException>(
            () => storage.SaveAsync(new MemoryStream(), ReportFileValidator.PdfContentType));

        Assert.Equal(ReportFileValidationFailure.EmptyFile, exception.Failure);
        Assert.Empty(FilesIn(root.StoragePath));
    }

    [Fact]
    public async Task Delete_Removes_The_Stored_File_Once()
    {
        using var root = new TemporaryStorageRoot();
        var storage = CreateStorage(root.StoragePath);
        var stored = await storage.SaveAsync(new MemoryStream(PdfBytes), ReportFileValidator.PdfContentType);

        var first = await storage.DeleteAsync(stored.StoredFileName);
        var second = await storage.DeleteAsync(stored.StoredFileName);

        Assert.True(first);
        Assert.False(second);
        Assert.False(File.Exists(Path.Combine(root.StoragePath, stored.StoredFileName)));
    }

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("..\\outside.pdf")]
    [InlineData("nested/report.pdf")]
    [InlineData("..")]
    public async Task Delete_Refuses_A_Name_That_Is_Not_A_Plain_File_Name(string storedFileName)
    {
        using var root = new TemporaryStorageRoot();
        var storage = CreateStorage(root.StoragePath);
        Directory.CreateDirectory(root.StoragePath);

        // A file the delete must never reach: it sits beside the storage root, not inside it.
        var outsidePath = Path.Combine(root.RootPath, "outside.pdf");
        await File.WriteAllBytesAsync(outsidePath, PdfBytes);

        await Assert.ThrowsAsync<ArgumentException>(() => storage.DeleteAsync(storedFileName));

        Assert.True(File.Exists(outsidePath));
    }

    private static LocalReportStorage CreateStorage(string rootPath) =>
        new(Options.Create(new ReportStorageOptions { RootPath = rootPath }));

    private static string[] FilesIn(string directory) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory) : [];

    /// <summary>A throw-away layout with a storage root and room for a file beside it.</summary>
    private sealed class TemporaryStorageRoot : IDisposable
    {
        public TemporaryStorageRoot()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "MediPassPlus.Tests",
                "reports",
                Guid.NewGuid().ToString("N"));

            StoragePath = Path.Combine(RootPath, "storage");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string StoragePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}

using MediPassPlus.Application.Records;

namespace MediPassPlus.Application.Tests.Records;

/// <summary>
/// The upload allow-list: PDF, JPEG and PNG only, at most 10 MB, and the leading bytes must match the declared type
/// (FR-REC-05, FR-REC-06, NFR-SEC-09).
/// </summary>
public sealed class ReportFileValidatorTests
{
    private static readonly byte[] PdfHeader = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Theory]
    [InlineData("application/pdf", ".pdf")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    public void Accepted_Type_With_Matching_Bytes_Is_Stored_Under_Its_Extension(string contentType, string extension)
    {
        var result = ReportFileValidator.Validate(contentType, 4_096, HeaderFor(contentType));

        Assert.True(result.IsValid);
        Assert.Equal(ReportFileValidationFailure.None, result.Failure);
        Assert.Equal(contentType, result.ContentType);
        Assert.Equal(extension, result.FileExtension);
    }

    [Theory]
    [InlineData("APPLICATION/PDF")]
    [InlineData("  application/pdf  ")]
    [InlineData("application/pdf; charset=binary")]
    public void Media_Type_Is_Matched_Case_Insensitively_And_Without_Parameters(string declaredContentType)
    {
        var result = ReportFileValidator.Validate(declaredContentType, 1_024, PdfHeader);

        Assert.True(result.IsValid);
        Assert.Equal(ReportFileValidator.PdfContentType, result.ContentType);
        Assert.Equal(".pdf", result.FileExtension);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Empty_Or_Negative_Size_Is_Refused(long sizeBytes)
    {
        var result = ReportFileValidator.Validate(ReportFileValidator.PdfContentType, sizeBytes, PdfHeader);

        Assert.False(result.IsValid);
        Assert.Equal(ReportFileValidationFailure.EmptyFile, result.Failure);

        // A refusal never carries a media type, so a caller cannot store a guess.
        Assert.Equal(string.Empty, result.ContentType);
        Assert.Equal(string.Empty, result.FileExtension);
    }

    [Fact]
    public void File_At_The_Limit_Is_Accepted()
    {
        var result = ReportFileValidator.Validate(
            ReportFileValidator.PdfContentType,
            ReportFileValidator.MaxSizeBytes,
            PdfHeader);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void File_One_Byte_Past_The_Limit_Is_Refused()
    {
        var result = ReportFileValidator.Validate(
            ReportFileValidator.PdfContentType,
            ReportFileValidator.MaxSizeBytes + 1,
            PdfHeader);

        Assert.False(result.IsValid);
        Assert.Equal(ReportFileValidationFailure.TooLarge, result.Failure);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    [InlineData("image/gif")]
    [InlineData("application/pdfx")]
    [InlineData("pdf")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Unsupported_Media_Type_Is_Refused(string? declaredContentType)
    {
        var result = ReportFileValidator.Validate(declaredContentType, 1_024, PdfHeader);

        Assert.False(result.IsValid);
        Assert.Equal(ReportFileValidationFailure.UnsupportedContentType, result.Failure);
    }

    [Fact]
    public void Allowed_Type_With_Another_Format_Bytes_Is_Refused()
    {
        // The client declares a PDF and sends a PNG: the declaration is worthless, the bytes are the evidence.
        var result = ReportFileValidator.Validate(
            ReportFileValidator.PdfContentType,
            1_024,
            PngHeader);

        Assert.False(result.IsValid);
        Assert.Equal(ReportFileValidationFailure.ContentMismatch, result.Failure);
        Assert.Equal(string.Empty, result.ContentType);
    }

    [Fact]
    public void Allowed_Type_With_Text_Bytes_Is_Refused()
    {
        var text = "%NOT A PDF"u8;

        var result = ReportFileValidator.Validate(ReportFileValidator.PdfContentType, 1_024, text);

        Assert.False(result.IsValid);
        Assert.Equal(ReportFileValidationFailure.ContentMismatch, result.Failure);
    }

    [Fact]
    public void Truncated_Header_Is_A_Mismatch()
    {
        // Three bytes cannot contain the five-byte %PDF- signature, and a short header is never read as an accept.
        var truncated = PdfHeader[..3];

        var result = ReportFileValidator.Validate(ReportFileValidator.PdfContentType, 3, truncated);

        Assert.False(result.IsValid);
        Assert.Equal(ReportFileValidationFailure.ContentMismatch, result.Failure);
    }

    [Fact]
    public void Empty_Header_Is_A_Mismatch()
    {
        var result = ReportFileValidator.Validate(ReportFileValidator.PdfContentType, 1_024, ReadOnlySpan<byte>.Empty);

        Assert.False(result.IsValid);
        Assert.Equal(ReportFileValidationFailure.ContentMismatch, result.Failure);
    }

    [Fact]
    public void Signature_Only_File_Is_Accepted()
    {
        // Five bytes are enough for a PDF: the validator decides the format, the endpoint decides nothing else.
        var result = ReportFileValidator.Validate(ReportFileValidator.PdfContentType, 5, PdfHeader[..5]);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(ReportFileValidator.PdfContentType, ".pdf")]
    [InlineData(ReportFileValidator.JpegContentType, ".jpg")]
    [InlineData(ReportFileValidator.PngContentType, ".png")]
    public void Extension_For_Returns_The_Storage_Extension(string contentType, string expected)
    {
        Assert.Equal(expected, ReportFileValidator.ExtensionFor(contentType));
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("image/gif")]
    [InlineData("")]
    [InlineData(null)]
    public void Extension_For_Refuses_An_Unsupported_Type(string? contentType)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReportFileValidator.ExtensionFor(contentType));
    }

    [Fact]
    public void The_Limit_Is_Ten_Megabytes()
    {
        Assert.Equal(10L * 1024 * 1024, ReportFileValidator.MaxSizeBytes);
    }

    private static ReadOnlySpan<byte> HeaderFor(string contentType) => contentType switch
    {
        ReportFileValidator.PdfContentType => PdfHeader,
        ReportFileValidator.JpegContentType => JpegHeader,
        ReportFileValidator.PngContentType => PngHeader,
        _ => throw new ArgumentOutOfRangeException(nameof(contentType))
    };
}

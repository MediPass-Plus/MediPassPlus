namespace MediPassPlus.Application.Records;

/// <summary>
/// The single check every report upload must pass before a byte is written to storage (FR-REC-06, NFR-SEC-09). It is a
/// pure function with no dependencies, so the upload endpoint, the storage layer and the tests share one definition of
/// "allowed file" instead of three.
/// <para>
/// The media type is taken from the request, but the **evidence** is the file's leading bytes: a client controls both
/// the declared type and the reported length, so a declaration that the bytes do not support is refused rather than
/// stored. The size check is repeated while the bytes are copied (see the storage implementation), because the
/// reported length is only a claim.
/// </para>
/// </summary>
public static class ReportFileValidator
{
    /// <summary>The largest accepted file: 10 MB (10,485,760 bytes), the FR-REC-05 / NFR-SEC-09 limit.</summary>
    public const long MaxSizeBytes = 10L * 1024 * 1024;

    /// <summary>
    /// How many leading bytes <see cref="Validate"/> needs. The longest supported signature is PNG's eight-byte magic
    /// number, so eight bytes are enough to decide every supported format.
    /// </summary>
    public const int MaxSignatureLength = 8;

    /// <summary>The PDF media type.</summary>
    public const string PdfContentType = "application/pdf";

    /// <summary>The JPEG media type.</summary>
    public const string JpegContentType = "image/jpeg";

    /// <summary>The PNG media type.</summary>
    public const string PngContentType = "image/png";

    private static readonly byte[] PdfSignature = [0x25, 0x50, 0x44, 0x46, 0x2D];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Validates the declared media type, the reported size and the file's leading bytes.
    /// </summary>
    /// <param name="declaredContentType">
    /// The media type the client declared for the part. Case is ignored and a parameter suffix such as
    /// <c>; charset=binary</c> is dropped before the comparison; <c>image/jpg</c> is accepted as an alias for
    /// <c>image/jpeg</c> and normalized to it. Nothing else is inferred from the declaration.
    /// </param>
    /// <param name="sizeBytes">The reported file length. Zero, a negative value or a length past the limit is refused.</param>
    /// <param name="header">
    /// The first bytes of the file, at most <see cref="MaxSignatureLength"/>. Fewer bytes than the declared format's
    /// signature is a mismatch, never an accept.
    /// </param>
    /// <returns>The accepted media type and storage extension, or the refusal reason.</returns>
    public static ReportFileValidationResult Validate(
        string? declaredContentType,
        long sizeBytes,
        ReadOnlySpan<byte> header)
    {
        if (sizeBytes <= 0)
        {
            return ReportFileValidationResult.Rejected(ReportFileValidationFailure.EmptyFile);
        }

        if (sizeBytes > MaxSizeBytes)
        {
            return ReportFileValidationResult.Rejected(ReportFileValidationFailure.TooLarge);
        }

        if (!TryResolve(declaredContentType, out var format))
        {
            return ReportFileValidationResult.Rejected(ReportFileValidationFailure.UnsupportedContentType);
        }

        if (!header.StartsWith(format.Signature))
        {
            return ReportFileValidationResult.Rejected(ReportFileValidationFailure.ContentMismatch);
        }

        return ReportFileValidationResult.Allowed(format.ContentType, format.Extension);
    }

    /// <summary>
    /// Returns the storage extension for an accepted media type: <c>.pdf</c>, <c>.jpg</c> or <c>.png</c>. The
    /// extension is always derived from the validated type, never from the client's file name (FR-REC-06).
    /// </summary>
    /// <param name="contentType">A media type, in any casing and with an optional parameter suffix.</param>
    /// <returns>The extension, including the leading dot.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The media type is not one of the three supported formats.</exception>
    public static string ExtensionFor(string? contentType)
    {
        if (!TryResolve(contentType, out var format))
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentType),
                "Only the PDF, JPEG and PNG media types have a storage extension.");
        }

        return format.Extension;
    }

    private static bool TryResolve(string? contentType, out ReportFileFormat format)
    {
        var normalized = Normalize(contentType);

        format = normalized switch
        {
            PdfContentType => new ReportFileFormat(PdfContentType, ".pdf", PdfSignature),
            JpegContentType => new ReportFileFormat(JpegContentType, ".jpg", JpegSignature),

            // "image/jpg" is not the registered JPEG media type, but clients send it often. It is accepted and
            // normalized to "image/jpeg" so the stored metadata and the extension stay canonical (FR-REC-06).
            "image/jpg" => new ReportFileFormat(JpegContentType, ".jpg", JpegSignature),
            PngContentType => new ReportFileFormat(PngContentType, ".png", PngSignature),
            _ => default
        };

        return format.Signature is not null;
    }

    private static string? Normalize(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        // A multipart part may carry parameters ("application/pdf; charset=binary"). Only the media type itself takes
        // part in the decision, and a client cannot widen the allow-list by appending one.
        var separator = contentType.IndexOf(';', StringComparison.Ordinal);
        var mediaType = separator >= 0 ? contentType[..separator] : contentType;

        return mediaType.Trim().ToLowerInvariant();
    }

    private readonly record struct ReportFileFormat(string ContentType, string Extension, byte[] Signature);
}

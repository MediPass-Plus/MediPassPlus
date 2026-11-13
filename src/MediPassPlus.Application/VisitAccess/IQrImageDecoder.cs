namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Decodes the QR payload from an uploaded camera image in memory. The image is never stored, and the decoded
/// payload is treated as a secret exactly like a token pasted into the JSON body (MP-97, ADR-0001).
/// </summary>
public interface IQrImageDecoder
{
    /// <summary>
    /// Decodes the first readable QR code in the image.
    /// </summary>
    /// <param name="image">The encoded image bytes. The caller has already enforced the upload size limit.</param>
    /// <returns>The decoded payload, or null when the image is corrupt, too large or contains no QR code.</returns>
    string? Decode(ReadOnlySpan<byte> image);
}

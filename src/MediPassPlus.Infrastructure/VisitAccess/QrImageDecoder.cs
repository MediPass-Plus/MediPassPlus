using MediPassPlus.Application.VisitAccess;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;

namespace MediPassPlus.Infrastructure.VisitAccess;

/// <summary>
/// Decodes a QR code from an uploaded camera image in memory with ZXing.Net and ImageSharp. Corrupt, unsupported or
/// QR-free images answer null instead of an exception, so the caller can return one stable refusal (MP-97).
/// </summary>
public sealed class QrImageDecoder : IQrImageDecoder
{
    /// <inheritdoc />
    public string? Decode(ReadOnlySpan<byte> image)
    {
        if (image.IsEmpty)
        {
            return null;
        }

        try
        {
            using var bitmap = Image.Load<Rgba32>(image);

            var reader = new ZXing.ImageSharp.BarcodeReader<Rgba32>
            {
                AutoRotate = true
            };

            reader.Options.TryHarder = true;
            reader.Options.PossibleFormats = [BarcodeFormat.QR_CODE];

            return reader.Decode(bitmap)?.Text;
        }
        catch (ImageFormatException)
        {
            // Unknown format or corrupt content: the upload is not a usable image.
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            // A recognised container whose pixel format this build cannot decode.
            return null;
        }
    }
}

using QRCoder;
using MediPassPlus.Application.VisitAccess;

namespace MediPassPlus.Infrastructure.VisitAccess;

/// <summary>
/// Renders a QR payload as a PNG data URI with QRCoder's fully managed PNG encoder. No System.Drawing or native
/// image library is involved, so the renderer works on every deployment target (MP-97, ADR-0001).
/// </summary>
public sealed class QrCodeRenderer : IQrCodeRenderer
{
    /// <summary>The module size in pixels. A 33-module QR code renders at 264x264, which scans well on screen.</summary>
    private const int PixelsPerModule = 8;

    /// <inheritdoc />
    public string RenderDataUri(string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);

        return $"data:image/png;base64,{Convert.ToBase64String(png.GetGraphic(PixelsPerModule))}";
    }
}

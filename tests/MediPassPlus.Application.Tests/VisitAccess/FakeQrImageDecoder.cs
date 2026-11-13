using MediPassPlus.Application.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

internal sealed class FakeQrImageDecoder : IQrImageDecoder
{
    public string? DecodedToken { get; set; }

    public int? LastImageLength { get; private set; }

    public string? Decode(ReadOnlySpan<byte> image)
    {
        LastImageLength = image.Length;
        return DecodedToken;
    }
}

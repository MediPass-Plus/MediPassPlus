using MediPassPlus.Application.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

internal sealed class FakeQrCodeRenderer : IQrCodeRenderer
{
    public const string DataUri = "data:image/png;base64,ZmFrZQ==";

    public string? LastPayload { get; private set; }

    public string RenderDataUri(string payload)
    {
        LastPayload = payload;
        return DataUri;
    }
}

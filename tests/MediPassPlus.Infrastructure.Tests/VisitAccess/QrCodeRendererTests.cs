using MediPassPlus.Infrastructure.VisitAccess;

namespace MediPassPlus.Infrastructure.Tests.VisitAccess;

public class QrCodeRendererTests
{
    [Fact]
    public void RenderDataUri_ReturnsAPngDataUriThatTheDecoderReadsBack()
    {
        const string payload = "a-round-trip-qr-token";
        var renderer = new QrCodeRenderer();
        var decoder = new QrImageDecoder();

        var dataUri = renderer.RenderDataUri(payload);

        Assert.StartsWith("data:image/png;base64,", dataUri, StringComparison.Ordinal);

        var base64 = dataUri["data:image/png;base64,".Length..];
        var png = Convert.FromBase64String(base64);
        Assert.Equal(payload, decoder.Decode(png));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    [InlineData(null)]
    public void RenderDataUri_WithoutAPayload_Throws(string? payload)
    {
        var renderer = new QrCodeRenderer();

        Assert.ThrowsAny<ArgumentException>(() => renderer.RenderDataUri(payload!));
    }

    [Fact]
    public void Decode_WhenTheBytesAreNotAnImage_ReturnsNull()
    {
        var decoder = new QrImageDecoder();

        Assert.Null(decoder.Decode([1, 2, 3, 4, 5]));
    }

    [Fact]
    public void Decode_WhenTheImageIsEmpty_ReturnsNull()
    {
        var decoder = new QrImageDecoder();

        Assert.Null(decoder.Decode([]));
    }
}

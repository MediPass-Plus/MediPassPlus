namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Renders a QR payload as a PNG image that can be displayed by the doctor. The renderer works only from the
/// one-time plaintext token; the token itself is never stored (ADR-0001, MP-97).
/// </summary>
public interface IQrCodeRenderer
{
    /// <summary>
    /// Renders the payload as a PNG data URI suitable for an <c>img</c> element.
    /// </summary>
    /// <param name="payload">The plaintext QR token. It must never be logged or persisted.</param>
    /// <returns>A <c>data:image/png;base64,...</c> URI.</returns>
    string RenderDataUri(string payload);
}

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Scans a patient-presented QR token, either as text or as a camera image, and returns the doctor and clinic
/// details the patient must see before deciding. Scanning never consumes the token; the decision does
/// (UC-12, FR-ACS-02, FR-PRV-07).
/// </summary>
public interface IAccessRequestScanService
{
    /// <summary>Resolves a token presented as text.</summary>
    /// <param name="token">The plaintext token from the request body.</param>
    /// <param name="patientUserId">The authenticated patient's user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request details or a refusal reason.</returns>
    Task<AccessRequestScanResult> ScanAsync(
        string token,
        Guid patientUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Decodes a QR code from an uploaded image and resolves it as a token.</summary>
    /// <param name="image">The encoded image bytes.</param>
    /// <param name="patientUserId">The authenticated patient's user account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request details or a refusal reason.</returns>
    Task<AccessRequestScanResult> ScanImageAsync(
        byte[] image,
        Guid patientUserId,
        CancellationToken cancellationToken = default);
}

using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Scans a QR token for the authenticated patient. The lookup is by the stored hash, the patient is checked
/// against the appointment on that request, and the token is never consumed by a scan (UC-12, FR-ACS-06,
/// FR-ACS-11, ADR-0001).
/// </summary>
public sealed class AccessRequestScanService : IAccessRequestScanService
{
    /// <summary>The largest camera image accepted by the scan-image endpoint, in bytes.</summary>
    public const int MaxImageBytes = 5 * 1024 * 1024;

    private readonly IAccessRequestScanStore _store;
    private readonly IQrImageDecoder _imageDecoder;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="AccessRequestScanService"/> class.</summary>
    /// <param name="store">The read-only lookup that resolves the token hash to its request and scope.</param>
    /// <param name="imageDecoder">Decodes a QR code from an uploaded camera image.</param>
    /// <param name="timeProvider">The clock used for the token and visit checks.</param>
    public AccessRequestScanService(
        IAccessRequestScanStore store,
        IQrImageDecoder imageDecoder,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(imageDecoder);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _imageDecoder = imageDecoder;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<AccessRequestScanResult> ScanAsync(
        string token,
        Guid patientUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var context = await _store.FindByTokenHashAsync(
            QrTokenGenerator.Hash(token),
            cancellationToken);

        if (context is null)
        {
            return AccessRequestScanResult.Denied(AccessRequestScanFailure.TokenNotFound);
        }

        // An unknown token and another patient's token must answer identically; otherwise the response would
        // reveal that a token exists and who owns it (FR-ACS-06, NFR-PRI-01).
        if (context.Request.PatientId != patientUserId
            || context.Appointment.PatientId != patientUserId)
        {
            return AccessRequestScanResult.Denied(AccessRequestScanFailure.TokenNotFound);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        if (!context.Credential.IsUsableAt(now))
        {
            return AccessRequestScanResult.Denied(AccessRequestScanFailure.TokenUnusable);
        }

        if (context.Request.Status != AccessRequestStatus.Pending)
        {
            return AccessRequestScanResult.Denied(AccessRequestScanFailure.TokenUnusable);
        }

        // UC-12 requires an appointment that is in progress. A completed, cancelled or never-started visit can no
        // longer produce a grant, so a stale token answers 410 instead of showing an approval that would fail.
        if (context.Appointment.Status is not (AppointmentStatus.CheckedIn or AppointmentStatus.InVisit))
        {
            return AccessRequestScanResult.Denied(AccessRequestScanFailure.VisitNotOpen);
        }

        return AccessRequestScanResult.Success(new AccessRequestScanResponse(
            context.Request.Id,
            context.Appointment.Id,
            context.DoctorName,
            context.DoctorSpecialty,
            context.ClinicName,
            context.ClinicAddress,
            context.AppointmentStartsAtUtc,
            context.Credential.ExpiresAtUtc));
    }

    /// <inheritdoc />
    public async Task<AccessRequestScanResult> ScanImageAsync(
        byte[] image,
        Guid patientUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.Length == 0 || image.Length > MaxImageBytes)
        {
            return AccessRequestScanResult.Denied(AccessRequestScanFailure.ImageNotReadable);
        }

        var token = _imageDecoder.Decode(image);
        if (string.IsNullOrWhiteSpace(token))
        {
            return AccessRequestScanResult.Denied(AccessRequestScanFailure.ImageNotReadable);
        }

        return await ScanAsync(token, patientUserId, cancellationToken);
    }
}

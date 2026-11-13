namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The doctor and clinic details a patient sees before approving a scanned QR code. The token itself is never
/// echoed back, and no medical content is included (UC-12, US-20).
/// </summary>
/// <param name="AccessRequestId">The pending request the token proves.</param>
/// <param name="AppointmentId">The appointment the request belongs to.</param>
/// <param name="DoctorName">The doctor's display name.</param>
/// <param name="DoctorSpecialty">The doctor's specialty.</param>
/// <param name="ClinicName">The clinic that owns the doctor.</param>
/// <param name="ClinicAddress">The clinic's address.</param>
/// <param name="AppointmentStartsAtUtc">When the appointment's slot starts, in UTC.</param>
/// <param name="ExpiresAtUtc">When the scanned token stops being valid, in UTC.</param>
public sealed record AccessRequestScanResponse(
    Guid AccessRequestId,
    int AppointmentId,
    string DoctorName,
    string DoctorSpecialty,
    string ClinicName,
    string ClinicAddress,
    DateTime AppointmentStartsAtUtc,
    DateTime ExpiresAtUtc);

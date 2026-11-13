using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The state a QR scan resolves from the stored token hash: the credential, its request and appointment, and the
/// doctor and clinic details the patient must see before approving (UC-12, FR-ACS-06).
/// </summary>
/// <param name="Credential">The credential whose hash matched the presented token.</param>
/// <param name="Request">The pending request the credential proves.</param>
/// <param name="Appointment">The appointment that fixes the request's scope and status.</param>
/// <param name="DoctorName">The doctor's display name.</param>
/// <param name="DoctorSpecialty">The doctor's specialty.</param>
/// <param name="ClinicName">The clinic that owns the doctor.</param>
/// <param name="ClinicAddress">The clinic's address.</param>
/// <param name="AppointmentStartsAtUtc">When the appointment's slot starts, in UTC.</param>
public sealed record AccessRequestScanContext(
    VisitAccessCredential Credential,
    AccessRequest Request,
    Appointment Appointment,
    string DoctorName,
    string DoctorSpecialty,
    string ClinicName,
    string ClinicAddress,
    DateTime AppointmentStartsAtUtc);

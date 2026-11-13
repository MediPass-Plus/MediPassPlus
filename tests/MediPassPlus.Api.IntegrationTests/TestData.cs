using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>An account created for one integration test and the token that authenticates it.</summary>
/// <param name="Id">The account identifier.</param>
/// <param name="Email">The account email.</param>
/// <param name="Token">A signed bearer token for the account.</param>
public sealed record TestUser(Guid Id, string Email, string Token);

/// <summary>The seeded visit graph a decision test needs, including the pending request and QR credential.</summary>
/// <param name="DoctorUser">The doctor's account and token.</param>
/// <param name="PatientUser">The patient's account and token.</param>
/// <param name="Clinic">The clinic that owns the doctor.</param>
/// <param name="Doctor">The doctor profile linked to the appointment.</param>
/// <param name="Slot">The doctor slot that makes the appointment today.</param>
/// <param name="Appointment">The appointment being answered.</param>
/// <param name="Request">The pending access request, or null when the scenario starts before start-visit.</param>
/// <param name="Credential">The live QR credential, or null when the scenario starts before start-visit.</param>
/// <param name="QrToken">
/// The plaintext QR token whose SHA-256 hash is stored, or null when the scenario starts before start-visit. It is
/// never persisted and exists only so an HTTP test can present it (MP-97).
/// </param>
/// <param name="CodeCredential">
/// The live numeric-code credential when the scenario seeded a code, or null.
/// </param>
public sealed record VisitScenario(
    TestUser DoctorUser,
    TestUser PatientUser,
    Clinic Clinic,
    Doctor Doctor,
    DoctorSlot Slot,
    Appointment Appointment,
    AccessRequest? Request,
    VisitAccessCredential? Credential,
    string? QrToken,
    VisitAccessCredential? CodeCredential = null);

/// <summary>
/// The seeded graph a booking test needs: a free future slot, an appointment on its own slot, a second free slot
/// for rescheduling, and a second doctor with a slot for the cross-doctor rule.
/// </summary>
/// <param name="DoctorUser">The doctor's account and token.</param>
/// <param name="PatientUser">The patient's account and token.</param>
/// <param name="Clinic">The clinic that owns the doctor.</param>
/// <param name="Doctor">The doctor profile.</param>
/// <param name="FreeSlot">A free future slot of <paramref name="Doctor"/>.</param>
/// <param name="AppointmentSlot">The slot occupied by <paramref name="Appointment"/>.</param>
/// <param name="Appointment">The patient's existing appointment.</param>
/// <param name="TargetSlot">A second free future slot of <paramref name="Doctor"/>.</param>
/// <param name="OtherDoctorUser">A second doctor's account and token.</param>
/// <param name="OtherDoctor">A second doctor profile.</param>
/// <param name="OtherDoctorSlot">A free future slot owned by <paramref name="OtherDoctor"/>.</param>
public sealed record BookingScenario(
    TestUser DoctorUser,
    TestUser PatientUser,
    Clinic Clinic,
    Doctor Doctor,
    DoctorSlot FreeSlot,
    DoctorSlot AppointmentSlot,
    Appointment Appointment,
    DoctorSlot TargetSlot,
    TestUser OtherDoctorUser,
    Doctor OtherDoctor,
    DoctorSlot OtherDoctorSlot);

/// <summary>
/// The seeded graph a check-in or completion test needs: a doctor, a patient, a same-clinic staff member, a staff
/// member in another clinic, and one appointment in the doctor's clinic.
/// </summary>
/// <param name="StaffUser">The staff account in the appointment's clinic.</param>
/// <param name="OtherClinicStaffUser">A staff account in a different clinic.</param>
/// <param name="DoctorUser">The doctor's account and token.</param>
/// <param name="PatientUser">The patient's account and token.</param>
/// <param name="Clinic">The clinic that owns the doctor and the staff member.</param>
/// <param name="OtherClinic">A second clinic that owns another staff member.</param>
/// <param name="Doctor">The doctor profile.</param>
/// <param name="OtherClinicDoctor">A doctor profile in the second clinic.</param>
/// <param name="Slot">The appointment's slot.</param>
/// <param name="Appointment">The appointment being checked in or completed.</param>
public sealed record AppointmentStatusScenario(
    TestUser StaffUser,
    TestUser OtherClinicStaffUser,
    TestUser DoctorUser,
    TestUser OtherClinicDoctorUser,
    TestUser PatientUser,
    Clinic Clinic,
    Clinic OtherClinic,
    Doctor Doctor,
    Doctor OtherClinicDoctor,
    DoctorSlot Slot,
    Appointment Appointment);

/// <summary>
/// The seeded graph a doctor schedule test needs: the caller's today appointments, a cancelled one, another
/// doctor's appointment and one on the next clinic-local day.
/// </summary>
/// <param name="DoctorUser">The authenticated doctor's account and token.</param>
/// <param name="OtherDoctorUser">A second doctor's account and token.</param>
/// <param name="Clinic">The shared clinic.</param>
/// <param name="Doctor">The authenticated doctor profile.</param>
/// <param name="OtherDoctor">The second doctor profile.</param>
/// <param name="BookedAppointment">A booked appointment of the authenticated doctor today.</param>
/// <param name="CheckedInAppointment">A checked-in appointment of the authenticated doctor today.</param>
/// <param name="CancelledAppointment">A cancelled appointment that must not appear.</param>
/// <param name="OtherDoctorAppointment">Another doctor's appointment that must not appear.</param>
/// <param name="TomorrowAppointment">The authenticated doctor's appointment tomorrow.</param>
public sealed record DoctorScheduleScenario(
    TestUser DoctorUser,
    TestUser OtherDoctorUser,
    Clinic Clinic,
    Doctor Doctor,
    Doctor OtherDoctor,
    Appointment BookedAppointment,
    Appointment CheckedInAppointment,
    Appointment CancelledAppointment,
    Appointment OtherDoctorAppointment,
    Appointment TomorrowAppointment);

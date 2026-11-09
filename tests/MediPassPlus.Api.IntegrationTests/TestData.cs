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
public sealed record VisitScenario(
    TestUser DoctorUser,
    TestUser PatientUser,
    Clinic Clinic,
    Doctor Doctor,
    DoctorSlot Slot,
    Appointment Appointment,
    AccessRequest? Request,
    VisitAccessCredential? Credential);

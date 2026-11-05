namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// The verified appointment scope that a visit-access decision is based on. It can only be produced after the
/// appointment, doctor and patient have been checked against each other.
/// </summary>
/// <param name="AppointmentId">The appointment the access belongs to.</param>
/// <param name="DoctorId">The doctor who owns the appointment.</param>
/// <param name="PatientId">The patient's user account identifier, taken from the appointment.</param>
public sealed record VisitAccessScope(int AppointmentId, int DoctorId, Guid PatientId);

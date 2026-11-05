using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Resolves a visit's real scope from the appointment instead of trusting identifiers supplied by a caller. The
/// database enforces that a stored request or grant keeps the appointment's doctor and patient, but it cannot verify
/// that the authenticated actor is that doctor or patient, so every read/write path must go through this check
/// (FR-ACS-06, ADR-0011 decision 8).
/// </summary>
public sealed class VisitAccessScopeService : IVisitAccessScopeService
{
    private readonly IVisitAccessScopeStore _store;

    /// <summary>Initializes a new instance of the <see cref="VisitAccessScopeService"/> class.</summary>
    /// <param name="store">The read-only lookups used to resolve the appointment and doctor.</param>
    public VisitAccessScopeService(IVisitAccessScopeStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public async Task<VisitAccessScopeResult> ResolveForDoctorAsync(
        int appointmentId,
        Guid doctorUserId,
        CancellationToken cancellationToken = default)
    {
        var doctor = await _store.FindDoctorByUserIdAsync(doctorUserId, cancellationToken);
        if (doctor is null)
        {
            return VisitAccessScopeResult.Denied(VisitAccessScopeFailure.DoctorNotFound);
        }

        var appointment = await _store.FindAppointmentAsync(appointmentId, cancellationToken);
        if (appointment is null)
        {
            return VisitAccessScopeResult.Denied(VisitAccessScopeFailure.AppointmentNotFound);
        }

        if (appointment.DoctorId != doctor.Id)
        {
            return VisitAccessScopeResult.Denied(VisitAccessScopeFailure.DoctorMismatch);
        }

        return VisitAccessScopeResult.Allowed(CreateScope(appointment));
    }

    /// <inheritdoc />
    public async Task<VisitAccessScopeResult> ResolveForPatientAsync(
        int appointmentId,
        Guid patientUserId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await _store.FindAppointmentAsync(appointmentId, cancellationToken);
        if (appointment is null)
        {
            return VisitAccessScopeResult.Denied(VisitAccessScopeFailure.AppointmentNotFound);
        }

        if (patientUserId == Guid.Empty || appointment.PatientId != patientUserId)
        {
            return VisitAccessScopeResult.Denied(VisitAccessScopeFailure.PatientMismatch);
        }

        return VisitAccessScopeResult.Allowed(CreateScope(appointment));
    }

    private static VisitAccessScope CreateScope(Appointment appointment) =>
        new(appointment.Id, appointment.DoctorId, appointment.PatientId);
}

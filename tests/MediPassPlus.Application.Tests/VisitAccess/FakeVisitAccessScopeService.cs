using MediPassPlus.Application.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

internal sealed class FakeVisitAccessScopeService : IVisitAccessScopeService
{
    public VisitAccessScopeResult DoctorResult { get; set; } =
        VisitAccessScopeResult.Denied(VisitAccessScopeFailure.AppointmentNotFound);

    public VisitAccessScopeResult PatientResult { get; set; } =
        VisitAccessScopeResult.Denied(VisitAccessScopeFailure.AppointmentNotFound);

    public bool WasCalled { get; private set; }

    public bool WasPatientCalled { get; private set; }

    public Task<VisitAccessScopeResult> ResolveForDoctorAsync(
        int appointmentId,
        Guid doctorUserId,
        CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return Task.FromResult(DoctorResult);
    }

    public Task<VisitAccessScopeResult> ResolveForPatientAsync(
        int appointmentId,
        Guid patientUserId,
        CancellationToken cancellationToken = default)
    {
        WasPatientCalled = true;
        return Task.FromResult(PatientResult);
    }

    public static FakeVisitAccessScopeService Allowing(VisitAccessScope scope) =>
        new() { DoctorResult = VisitAccessScopeResult.Allowed(scope) };

    public static FakeVisitAccessScopeService AllowingPatient(VisitAccessScope scope) =>
        new() { PatientResult = VisitAccessScopeResult.Allowed(scope) };
}

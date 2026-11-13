using MediPassPlus.Application.VisitAccess;

namespace MediPassPlus.Application.Tests.VisitAccess;

internal sealed class FakeAccessStateStore : IAccessStateStore
{
    public AccessStateContext? Context { get; set; }

    public bool WasLoaded { get; private set; }

    public int? LastAppointmentId { get; private set; }

    public Task<AccessStateContext?> LoadAsync(
        int appointmentId,
        int doctorId,
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        WasLoaded = true;
        LastAppointmentId = appointmentId;
        return Task.FromResult(Context);
    }
}

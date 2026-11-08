using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.Tests.VisitAccess;

internal sealed class FakeVisitAccessScopeStore : IVisitAccessScopeStore
{
    public Appointment? Appointment { get; set; }

    public Doctor? Doctor { get; set; }

    public Task<Appointment?> FindAppointmentAsync(int appointmentId, CancellationToken cancellationToken) =>
        Task.FromResult(
            Appointment is not null && Appointment.Id == appointmentId ? Appointment : null);

    public Task<Doctor?> FindDoctorByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(
            Doctor is not null && Doctor.UserId == userId ? Doctor : null);
}

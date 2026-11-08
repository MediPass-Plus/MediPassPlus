using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Application.Tests.VisitAccess;

public class VisitAccessScopeServiceTests
{
    [Fact]
    public async Task ResolveForDoctor_WhenDoctorOwnsAppointment_AllowsTheScope()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(appointment.DoctorId);
        var service = CreateService(appointment, doctor);

        var result = await service.ResolveForDoctorAsync(appointment.Id, doctor.UserId);

        Assert.True(result.IsAllowed);
        Assert.Equal(VisitAccessScopeFailure.None, result.Failure);
        Assert.NotNull(result.Scope);
        Assert.Equal(appointment.Id, result.Scope!.AppointmentId);
        Assert.Equal(doctor.Id, result.Scope.DoctorId);
        Assert.Equal(appointment.PatientId, result.Scope.PatientId);
    }

    [Fact]
    public async Task ResolveForDoctor_WhenDoctorProfileIsMissing_Denies()
    {
        var appointment = CreateAppointment();
        var service = CreateService(appointment);

        var result = await service.ResolveForDoctorAsync(appointment.Id, Guid.NewGuid());

        Assert.False(result.IsAllowed);
        Assert.Null(result.Scope);
        Assert.Equal(VisitAccessScopeFailure.DoctorNotFound, result.Failure);
    }

    [Fact]
    public async Task ResolveForDoctor_WhenAppointmentIsMissing_Denies()
    {
        var doctor = CreateDoctor(id: 7);
        var service = CreateService(doctor: doctor);

        var result = await service.ResolveForDoctorAsync(42, doctor.UserId);

        Assert.False(result.IsAllowed);
        Assert.Equal(VisitAccessScopeFailure.AppointmentNotFound, result.Failure);
    }

    [Fact]
    public async Task ResolveForDoctor_ForAnotherDoctorsAppointment_Denies()
    {
        var appointment = CreateAppointment(doctorId: 7);
        var otherDoctor = CreateDoctor(id: 8);
        var service = CreateService(appointment, otherDoctor);

        var result = await service.ResolveForDoctorAsync(appointment.Id, otherDoctor.UserId);

        Assert.False(result.IsAllowed);
        Assert.Equal(VisitAccessScopeFailure.DoctorMismatch, result.Failure);
    }

    [Fact]
    public async Task ResolveForPatient_WhenPatientOwnsAppointment_AllowsTheScope()
    {
        var appointment = CreateAppointment();
        var service = CreateService(appointment);

        var result = await service.ResolveForPatientAsync(appointment.Id, appointment.PatientId);

        Assert.True(result.IsAllowed);
        Assert.Equal(VisitAccessScopeFailure.None, result.Failure);
        Assert.NotNull(result.Scope);
        Assert.Equal(appointment.Id, result.Scope!.AppointmentId);
        Assert.Equal(appointment.DoctorId, result.Scope.DoctorId);
        Assert.Equal(appointment.PatientId, result.Scope.PatientId);
    }

    [Fact]
    public async Task ResolveForPatient_WhenAppointmentIsMissing_Denies()
    {
        var service = CreateService();

        var result = await service.ResolveForPatientAsync(42, Guid.NewGuid());

        Assert.False(result.IsAllowed);
        Assert.Equal(VisitAccessScopeFailure.AppointmentNotFound, result.Failure);
    }

    [Fact]
    public async Task ResolveForPatient_ForAnotherPatientsAppointment_Denies()
    {
        var appointment = CreateAppointment();
        var service = CreateService(appointment);

        var result = await service.ResolveForPatientAsync(appointment.Id, Guid.NewGuid());

        Assert.False(result.IsAllowed);
        Assert.Equal(VisitAccessScopeFailure.PatientMismatch, result.Failure);
    }

    [Fact]
    public async Task ResolveForPatient_WithAnEmptyUserId_Denies()
    {
        var appointment = CreateAppointment(patientId: Guid.Empty);
        var service = CreateService(appointment);

        var result = await service.ResolveForPatientAsync(appointment.Id, Guid.Empty);

        Assert.False(result.IsAllowed);
        Assert.Equal(VisitAccessScopeFailure.PatientMismatch, result.Failure);
    }

    private static VisitAccessScopeService CreateService(
        Appointment? appointment = null,
        Doctor? doctor = null) =>
        new(new FakeVisitAccessScopeStore
        {
            Appointment = appointment,
            Doctor = doctor
        });

    private static Appointment CreateAppointment(int doctorId = 7, Guid? patientId = null) =>
        new()
        {
            Id = 42,
            DoctorId = doctorId,
            PatientId = patientId ?? Guid.NewGuid()
        };

    private static Doctor CreateDoctor(int id) =>
        new()
        {
            Id = id,
            UserId = Guid.NewGuid()
        };
}

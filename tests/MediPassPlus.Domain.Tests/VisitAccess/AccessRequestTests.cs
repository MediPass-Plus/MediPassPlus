using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Domain.Tests.VisitAccess;

public class AccessRequestTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_StartsPendingWithTheAppointmentScope()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor();

        var request = AccessRequest.Create(appointment, doctor, Now);

        Assert.NotEqual(Guid.Empty, request.Id);
        Assert.Equal(appointment.Id, request.AppointmentId);
        Assert.Equal(doctor.Id, request.DoctorId);
        Assert.Equal(appointment.PatientId, request.PatientId);
        Assert.Equal(Now, request.CreatedAtUtc);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);
    }

    [Fact]
    public void Create_TakesThePatientFromTheAppointment()
    {
        var appointment = CreateAppointment(patientId: Guid.NewGuid());

        var request = AccessRequest.Create(appointment, CreateDoctor(), Now);

        Assert.Equal(appointment.PatientId, request.PatientId);
    }

    [Fact]
    public void Create_WhenAppointmentBelongsToAnotherDoctor_Throws()
    {
        var appointment = CreateAppointment(doctorId: 7);
        var otherDoctor = CreateDoctor(id: 8);

        Assert.Throws<InvalidOperationException>(
            () => AccessRequest.Create(appointment, otherDoctor, Now));
    }

    [Fact]
    public void Create_WithUnpersistedAppointment_Throws()
    {
        var appointment = CreateAppointment(id: 0);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => AccessRequest.Create(appointment, CreateDoctor(), Now));
    }

    [Fact]
    public void Create_WithUnpersistedDoctor_Throws()
    {
        var appointment = CreateAppointment();
        var doctor = CreateDoctor(id: 0);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => AccessRequest.Create(appointment, doctor, Now));
    }

    [Fact]
    public void Create_WithAnAppointmentThatHasNoPatient_Throws()
    {
        var appointment = CreateAppointment(patientId: Guid.Empty);

        Assert.Throws<ArgumentException>(
            () => AccessRequest.Create(appointment, CreateDoctor(), Now));
    }

    [Fact]
    public void Create_NullAppointment_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => AccessRequest.Create(null!, CreateDoctor(), Now));
    }

    [Fact]
    public void Create_NullDoctor_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => AccessRequest.Create(CreateAppointment(), null!, Now));
    }

    [Fact]
    public void Approve_FromPending_BecomesApproved()
    {
        var request = CreatePending();

        request.Approve();

        Assert.Equal(AccessRequestStatus.Approved, request.Status);
    }

    [Fact]
    public void Reject_FromPending_BecomesRejected()
    {
        var request = CreatePending();

        request.Reject();

        Assert.Equal(AccessRequestStatus.Rejected, request.Status);
    }

    [Fact]
    public void Approve_AfterRejection_Throws()
    {
        var request = CreatePending();
        request.Reject();

        Assert.Throws<InvalidOperationException>(request.Approve);
    }

    [Fact]
    public void Reject_AfterApproval_Throws()
    {
        var request = CreatePending();
        request.Approve();

        Assert.Throws<InvalidOperationException>(request.Reject);
    }

    [Fact]
    public void Approve_Twice_Throws()
    {
        var request = CreatePending();
        request.Approve();

        Assert.Throws<InvalidOperationException>(request.Approve);
    }

    private static AccessRequest CreatePending() =>
        AccessRequest.Create(CreateAppointment(), CreateDoctor(), Now);

    private static Appointment CreateAppointment(int id = 42, int doctorId = 7, Guid? patientId = null) =>
        new()
        {
            Id = id,
            DoctorId = doctorId,
            PatientId = patientId ?? Guid.NewGuid()
        };

    private static Doctor CreateDoctor(int id = 7) =>
        new()
        {
            Id = id,
            UserId = Guid.NewGuid()
        };
}

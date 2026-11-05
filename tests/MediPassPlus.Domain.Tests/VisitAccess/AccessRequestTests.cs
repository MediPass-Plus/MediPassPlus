using MediPassPlus.Domain.VisitAccess;

namespace MediPassPlus.Domain.Tests.VisitAccess;

public class AccessRequestTests
{
    [Fact]
    public void Create_StartsPendingWithTheSuppliedScope()
    {
        var now = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);
        var patientId = Guid.NewGuid();

        var request = AccessRequest.Create(42, 7, patientId, now);

        Assert.NotEqual(Guid.Empty, request.Id);
        Assert.Equal(42, request.AppointmentId);
        Assert.Equal(7, request.DoctorId);
        Assert.Equal(patientId, request.PatientId);
        Assert.Equal(now, request.CreatedAtUtc);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);
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

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    public void Create_WithInvalidScope_Throws(int appointmentId, int doctorId)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => AccessRequest.Create(appointmentId, doctorId, Guid.NewGuid(), DateTime.UtcNow));
    }

    [Fact]
    public void Create_WithEmptyPatientId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => AccessRequest.Create(1, 1, Guid.Empty, DateTime.UtcNow));
    }

    private static AccessRequest CreatePending() =>
        AccessRequest.Create(42, 7, Guid.NewGuid(), new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc));
}

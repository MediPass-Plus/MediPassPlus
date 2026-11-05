namespace MediPassPlus.Domain.VisitAccess;

/// <summary>
/// A doctor's request to access one patient's record for one appointment. The patient answers it once;
/// the secrets that prove the request belong to <see cref="VisitAccessCredential"/>.
/// </summary>
public sealed class AccessRequest
{
    private AccessRequest()
    {
    }

    private AccessRequest(Guid id, int appointmentId, int doctorId, Guid patientId, DateTime createdAtUtc)
    {
        Id = id;
        AppointmentId = appointmentId;
        DoctorId = doctorId;
        PatientId = patientId;
        CreatedAtUtc = createdAtUtc;
        Status = AccessRequestStatus.Pending;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; }

    /// <summary>The appointment whose record access is requested.</summary>
    public int AppointmentId { get; private set; }

    /// <summary>The doctor who started the visit.</summary>
    public int DoctorId { get; private set; }

    /// <summary>The patient's user account identifier.</summary>
    public Guid PatientId { get; private set; }

    /// <summary>When the doctor created the request, in UTC.</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Current lifecycle state.</summary>
    public AccessRequestStatus Status { get; private set; }

    /// <summary>
    /// Creates a pending request. One request is created per visit start; a new credential may replace an old one
    /// under the same request (US-22).
    /// </summary>
    /// <param name="appointmentId">The appointment being started.</param>
    /// <param name="doctorId">The doctor starting the visit.</param>
    /// <param name="patientId">The patient's user account identifier.</param>
    /// <param name="utcNow">The current UTC time.</param>
    public static AccessRequest Create(int appointmentId, int doctorId, Guid patientId, DateTime utcNow)
    {
        if (appointmentId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appointmentId));
        }

        if (doctorId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(doctorId));
        }

        if (patientId == Guid.Empty)
        {
            throw new ArgumentException("The patient id is required.", nameof(patientId));
        }

        return new AccessRequest(Guid.CreateVersion7(), appointmentId, doctorId, patientId, utcNow);
    }

    /// <summary>Records the patient's approval. Only a pending request can be approved.</summary>
    public void Approve()
    {
        EnsurePending();
        Status = AccessRequestStatus.Approved;
    }

    /// <summary>Records the patient's rejection. No grant is created for a rejected request.</summary>
    public void Reject()
    {
        EnsurePending();
        Status = AccessRequestStatus.Rejected;
    }

    private void EnsurePending()
    {
        if (Status != AccessRequestStatus.Pending)
        {
            throw new InvalidOperationException($"A request in state {Status} cannot change state.");
        }
    }
}

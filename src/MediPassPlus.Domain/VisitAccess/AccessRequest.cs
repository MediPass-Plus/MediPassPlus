using MediPassPlus.Domain.Clinical;

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
    /// Creates a pending request for the doctor who owns <paramref name="appointment"/>. The patient and the
    /// appointment scope are taken from the appointment itself, so a request can never point at another doctor's
    /// appointment or at another patient (FR-ACS-06, ADR-0011 decision 8). One request is created per visit start;
    /// a new credential may replace an old one under the same request (US-22).
    /// </summary>
    /// <param name="appointment">The appointment being started.</param>
    /// <param name="doctor">The doctor starting the visit; must own the appointment.</param>
    /// <param name="utcNow">The current UTC time.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The appointment or doctor is not persisted.</exception>
    /// <exception cref="ArgumentException">The appointment has no patient.</exception>
    /// <exception cref="InvalidOperationException">The appointment belongs to another doctor.</exception>
    public static AccessRequest Create(Appointment appointment, Doctor doctor, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(doctor);

        if (appointment.Id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(appointment), "The appointment must be persisted.");
        }

        if (doctor.Id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(doctor), "The doctor must be persisted.");
        }

        if (appointment.PatientId == Guid.Empty)
        {
            throw new ArgumentException("The appointment has no patient.", nameof(appointment));
        }

        if (appointment.DoctorId != doctor.Id)
        {
            throw new InvalidOperationException("The appointment belongs to another doctor.");
        }

        return new AccessRequest(Guid.CreateVersion7(), appointment.Id, doctor.Id, appointment.PatientId, utcNow);
    }

    /// <summary>
    /// Records the patient's approval. Only a pending request can be approved. A successful numeric-code verification
    /// by the owning doctor records the same approval, because the code is the patient's approval expressed out of
    /// band (ADR-0011 amendment 2026-11-12).
    /// </summary>
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

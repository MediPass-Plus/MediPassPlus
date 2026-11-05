namespace MediPassPlus.Domain.VisitAccess;

/// <summary>
/// A patient's time-limited permission for one doctor to read their record for one appointment.
/// The status is derived from the stored timestamps and is never persisted.
/// </summary>
public sealed class AccessGrant
{
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromHours(2);
    private static readonly TimeSpan PostVisitGrace = TimeSpan.FromMinutes(15);

    private AccessGrant()
    {
    }

    private AccessGrant(Guid id, AccessRequest request, DateTime grantedAtUtc)
    {
        Id = id;
        AccessRequestId = request.Id;
        AppointmentId = request.AppointmentId;
        DoctorId = request.DoctorId;
        PatientId = request.PatientId;
        GrantedAtUtc = grantedAtUtc;
        ExpiresAtUtc = grantedAtUtc.Add(MaximumLifetime);
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; }

    /// <summary>The approved request that produced this grant.</summary>
    public Guid AccessRequestId { get; private set; }

    /// <summary>The appointment the grant is scoped to.</summary>
    public int AppointmentId { get; private set; }

    /// <summary>The doctor the patient granted access to.</summary>
    public int DoctorId { get; private set; }

    /// <summary>The patient's user account identifier.</summary>
    public Guid PatientId { get; private set; }

    /// <summary>When the patient approved the request, in UTC.</summary>
    public DateTime GrantedAtUtc { get; private set; }

    /// <summary>The two-hour hard limit, calculated when the grant is created and never extended.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>
    /// The scheduled or actual end, in UTC. Null means only <see cref="ExpiresAtUtc"/> applies. A completed visit
    /// sets this 15 minutes in the future so the doctor can finish the record.
    /// </summary>
    public DateTime? EndedAtUtc { get; private set; }

    /// <summary>Why the grant ended. Null while the two-hour limit is the only end.</summary>
    public GrantEndReason? EndReason { get; private set; }

    /// <summary>Creates the grant for an approved request (FR-ACS-04).</summary>
    /// <param name="request">The approved access request.</param>
    /// <param name="utcNow">The current UTC time.</param>
    public static AccessGrant Create(AccessRequest request, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Status != AccessRequestStatus.Approved)
        {
            throw new InvalidOperationException("An access grant can only be created for an approved request.");
        }

        return new AccessGrant(Guid.CreateVersion7(), request, utcNow);
    }

    /// <summary>True while the grant has not reached its effective end.</summary>
    /// <param name="utcNow">The current UTC time.</param>
    public bool IsActiveAt(DateTime utcNow) => utcNow < EffectiveEndUtc;

    /// <summary>Returns the effective status at <paramref name="utcNow"/>.</summary>
    /// <param name="utcNow">The current UTC time.</param>
    public AccessGrantStatus StatusAt(DateTime utcNow)
    {
        if (utcNow < EffectiveEndUtc)
        {
            return AccessGrantStatus.Active;
        }

        return EndReason is GrantEndReason.VisitCompleted or GrantEndReason.PatientEnded
            ? AccessGrantStatus.Ended
            : AccessGrantStatus.Expired;
    }

    /// <summary>
    /// Schedules the end at 15 minutes after completion, capped by the two-hour limit. The grant stays active
    /// during the grace window. Repeated calls keep the earliest end.
    /// </summary>
    /// <param name="utcNow">The current UTC time.</param>
    public void CompleteVisit(DateTime utcNow)
    {
        EnsureActive(utcNow);

        var scheduledEnd = utcNow.Add(PostVisitGrace);
        var cappedEnd = scheduledEnd < ExpiresAtUtc ? scheduledEnd : ExpiresAtUtc;

        if (EndedAtUtc is null || cappedEnd < EndedAtUtc)
        {
            EndedAtUtc = cappedEnd;
        }

        EndReason = GrantEndReason.VisitCompleted;
    }

    /// <summary>Ends the grant immediately at the patient's request (FR-ACS-09).</summary>
    /// <param name="utcNow">The current UTC time.</param>
    public void EndByPatient(DateTime utcNow)
    {
        EnsureActive(utcNow);
        EndedAtUtc = utcNow;
        EndReason = GrantEndReason.PatientEnded;
    }

    private DateTime EffectiveEndUtc => EndedAtUtc ?? ExpiresAtUtc;

    private void EnsureActive(DateTime utcNow)
    {
        if (!IsActiveAt(utcNow))
        {
            throw new InvalidOperationException($"A grant in state {StatusAt(utcNow)} cannot change state.");
        }
    }
}

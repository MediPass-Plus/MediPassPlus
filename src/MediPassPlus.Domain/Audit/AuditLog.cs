namespace MediPassPlus.Domain.Audit;

/// <summary>
/// An append-only audit entry for a medical read/write or access-control event (ADR-0004).
/// The entity exposes no mutation methods; EF updates and deletes are rejected by an interceptor.
/// </summary>
public sealed class AuditLog
{
    private AuditLog()
    {
    }

    private AuditLog(
        Guid id,
        Guid userId,
        Guid patientId,
        AuditAction action,
        AuditEntityType entityType,
        string entityId,
        DateTimeOffset occurredAtUtc,
        string ipAddress,
        Guid? accessGrantId)
    {
        Id = id;
        UserId = userId;
        PatientId = patientId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        OccurredAtUtc = occurredAtUtc;
        IpAddress = ipAddress;
        AccessGrantId = accessGrantId;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; }

    /// <summary>The user who performed the action.</summary>
    public Guid UserId { get; private set; }

    /// <summary>The patient whose data was affected.</summary>
    public Guid PatientId { get; private set; }

    /// <summary>The controlled action that occurred.</summary>
    public AuditAction Action { get; private set; }

    /// <summary>The controlled type of entity affected by the action.</summary>
    public AuditEntityType EntityType { get; private set; }

    /// <summary>
    /// The affected entity identifier. Stored as text because the referenced entities use different key types
    /// (for example <see cref="Guid"/> and <see cref="int"/>).
    /// </summary>
    public string EntityId { get; private set; } = string.Empty;

    /// <summary>When the action occurred, in UTC.</summary>
    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>The connection IP address recorded for the action.</summary>
    public string IpAddress { get; private set; } = string.Empty;

    /// <summary>The access grant that authorized the action, when one applies.</summary>
    public Guid? AccessGrantId { get; private set; }

    /// <summary>
    /// Creates a new audit entry. Only identifiers and controlled values are accepted; medical content is never
    /// part of an audit row (ADR-0004 decision 3).
    /// </summary>
    /// <param name="userId">The user who performed the action.</param>
    /// <param name="patientId">The patient whose data was affected.</param>
    /// <param name="action">The controlled action that occurred.</param>
    /// <param name="entityType">The controlled type of entity affected.</param>
    /// <param name="entityId">The affected entity identifier.</param>
    /// <param name="ipAddress">The connection IP address.</param>
    /// <param name="occurredAtUtc">When the action occurred, in UTC.</param>
    /// <param name="accessGrantId">The authorizing grant, when one applies.</param>
    /// <returns>The new audit entry.</returns>
    /// <exception cref="ArgumentException">A required identifier or string is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An enum value or string length is not valid.</exception>
    public static AuditLog Create(
        Guid userId,
        Guid patientId,
        AuditAction action,
        AuditEntityType entityType,
        string entityId,
        string ipAddress,
        DateTimeOffset occurredAtUtc,
        Guid? accessGrantId = null)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A user identifier is required.", nameof(userId));
        }

        if (patientId == Guid.Empty)
        {
            throw new ArgumentException("A patient identifier is required.", nameof(patientId));
        }

        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown audit action.");
        }

        if (!Enum.IsDefined(entityType))
        {
            throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Unknown audit entity type.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        if (entityId.Length > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(entityId), "The entity identifier is too long.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

        if (ipAddress.Length > 45)
        {
            throw new ArgumentOutOfRangeException(nameof(ipAddress), "The IP address is too long.");
        }

        if (accessGrantId == Guid.Empty)
        {
            throw new ArgumentException("An access grant identifier cannot be empty.", nameof(accessGrantId));
        }

        return new AuditLog(
            Guid.CreateVersion7(),
            userId,
            patientId,
            action,
            entityType,
            entityId,
            occurredAtUtc,
            ipAddress,
            accessGrantId);
    }
}

using MediPassPlus.Domain.Audit;

namespace MediPassPlus.Application.Audit;

/// <summary>
/// The caller-supplied fields for one audit entry. The writer supplies the identifier and timestamp.
/// </summary>
/// <param name="UserId">The user who performed the action.</param>
/// <param name="PatientId">The patient whose data was affected.</param>
/// <param name="Action">The controlled action that occurred.</param>
/// <param name="EntityType">The controlled type of entity affected.</param>
/// <param name="EntityId">The affected entity identifier.</param>
/// <param name="IpAddress">The connection IP address.</param>
/// <param name="AccessGrantId">The authorizing grant, when one applies.</param>
public sealed record AuditEntry(
    Guid UserId,
    Guid PatientId,
    AuditAction Action,
    AuditEntityType EntityType,
    string EntityId,
    string IpAddress,
    Guid? AccessGrantId = null);

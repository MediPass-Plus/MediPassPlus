using MediPassPlus.Domain.Audit;

namespace MediPassPlus.Application.Audit;

/// <summary>
/// One audit row as the store returns it. It carries identifiers and controlled values only; medical content is never
/// part of an audit row (ADR-0004 decision 3).
/// </summary>
/// <param name="Id">The audit row identifier.</param>
/// <param name="UserId">The user who performed the action.</param>
/// <param name="Action">The controlled action that occurred.</param>
/// <param name="EntityType">The controlled type of entity affected.</param>
/// <param name="EntityId">The affected entity identifier.</param>
/// <param name="OccurredAtUtc">When the action occurred, in UTC.</param>
/// <param name="IpAddress">The connection IP address recorded for the action.</param>
/// <param name="AccessGrantId">The access grant that authorized the action, when one applies.</param>
public sealed record AuditLogRow(
    Guid Id,
    Guid UserId,
    AuditAction Action,
    AuditEntityType EntityType,
    string EntityId,
    DateTimeOffset OccurredAtUtc,
    string IpAddress,
    Guid? AccessGrantId);

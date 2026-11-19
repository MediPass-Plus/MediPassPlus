namespace MediPassPlus.Application.Audit;

/// <summary>
/// One entry of the patient's "who viewed my record" list: identifiers, action, entity, time and IP address only, with
/// no medical content (FR-PRV-02, FR-PRV-03, ADR-0004 decisions 3 and 5).
/// </summary>
/// <param name="Id">The audit row identifier.</param>
/// <param name="UserId">The user who performed the action: the doctor for a visit read, the patient for a self-read.</param>
/// <param name="Action">The controlled action name.</param>
/// <param name="EntityType">The controlled entity type name.</param>
/// <param name="EntityId">The identifier of the affected entity.</param>
/// <param name="OccurredAtUtc">When the action happened, in UTC.</param>
/// <param name="IpAddress">The connection IP address recorded for the action.</param>
/// <param name="AccessGrantId">The access grant that authorized the action, when one applies.</param>
public sealed record AuditLogEntryResponse(
    Guid Id,
    Guid UserId,
    string Action,
    string EntityType,
    string EntityId,
    DateTimeOffset OccurredAtUtc,
    string IpAddress,
    Guid? AccessGrantId);

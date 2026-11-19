using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.Audit;

/// <summary>
/// The patient's audit read model. Reading the list is deliberately not audited, so this service has no audit writer
/// and can never recurse into its own list (ADR-0004 decision 4). The rows are already scoped to the authenticated
/// patient by the store, and no medical content is mapped into the response (FR-PRV-03).
/// </summary>
public sealed class AuditLogQueryService : IAuditLogQueryService
{
    private readonly IAuditLogReader _reader;

    /// <summary>Initializes a new instance of the <see cref="AuditLogQueryService"/> class.</summary>
    /// <param name="reader">The read side that returns the patient's audit rows.</param>
    public AuditLogQueryService(IAuditLogReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _reader = reader;
    }

    /// <inheritdoc />
    public async Task<PagedResult<AuditLogEntryResponse>> GetForPatientAsync(
        Guid patientUserId,
        MyAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = await _reader.ReadForPatientAsync(
            patientUserId,
            query.GetSkip(),
            query.GetPageSize(),
            cancellationToken);

        return new PagedResult<AuditLogEntryResponse>
        {
            Items = page.Items.Select(ToResponse).ToList(),
            TotalCount = page.TotalCount,
            Page = query.Page,
            PageSize = query.GetPageSize()
        };
    }

    private static AuditLogEntryResponse ToResponse(AuditLogRow row) =>
        new(
            row.Id,
            row.UserId,
            row.Action.ToString(),
            row.EntityType.ToString(),
            row.EntityId,
            row.OccurredAtUtc,
            row.IpAddress,
            row.AccessGrantId);
}

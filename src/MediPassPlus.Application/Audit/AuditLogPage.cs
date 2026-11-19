namespace MediPassPlus.Application.Audit;

/// <summary>One page of a patient's audit rows and the total count for their whole history.</summary>
/// <param name="Items">The rows on the requested page, newest first.</param>
/// <param name="TotalCount">The number of rows the patient owns.</param>
public sealed record AuditLogPage(IReadOnlyList<AuditLogRow> Items, int TotalCount);

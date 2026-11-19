using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.Audit;

/// <summary>
/// The query string of the authenticated patient's "who viewed my record" list. Paging follows NFR-PRF-04: 20 rows
/// by default and at most 50 per page (FR-PRV-03).
/// </summary>
public sealed class MyAuditQuery : PageRequest
{
}

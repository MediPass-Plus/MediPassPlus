using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.Booking;

/// <summary>
/// The query string for the authenticated patient's appointment list. Paging follows NFR-PRF-04: 20 rows by default
/// and at most 50 per page (FR-BKG-06).
/// </summary>
public sealed class MyAppointmentsQuery : PageRequest
{
}

namespace MediPassPlus.Application.Booking;

/// <summary>
/// One page of the authenticated patient's appointments plus the total count the API needs to build the paged
/// response (NFR-PRF-04, FR-BKG-06).
/// </summary>
/// <param name="Items">The rows of the requested page, newest slot first.</param>
/// <param name="TotalCount">The total number of appointments in the patient's scope.</param>
public sealed record PatientAppointmentsPage(
    IReadOnlyList<PatientAppointmentRow> Items,
    int TotalCount);

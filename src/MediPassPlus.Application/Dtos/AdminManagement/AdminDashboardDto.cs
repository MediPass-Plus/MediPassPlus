namespace MediPassPlus.Application.Dtos.AdminManagement;

/// <summary>
/// Aggregate counts for the admin dashboard (FR-STF-08). It carries numbers only: no patient identifier, no name,
/// and no diagnosis, prescription, report or history data (FR-STF-09, FR-PRV-06).
/// </summary>
/// <param name="Users">The number of active accounts per role.</param>
/// <param name="Clinics">The number of clinics.</param>
/// <param name="Appointments">The number of appointments per status.</param>
public sealed record AdminDashboardDto(
    UserCountsDto Users,
    int Clinics,
    AppointmentCountsDto Appointments);

/// <summary>The number of active accounts per role.</summary>
public sealed record UserCountsDto(int Patients, int Doctors, int Staff, int Admins);

/// <summary>The number of appointments per status.</summary>
public sealed record AppointmentCountsDto(
    int Booked,
    int CheckedIn,
    int InVisit,
    int Completed,
    int Cancelled);
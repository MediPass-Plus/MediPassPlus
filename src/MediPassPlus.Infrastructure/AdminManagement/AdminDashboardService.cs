using MediPassPlus.Application.AdminManagement;
using MediPassPlus.Application.Dtos.AdminManagement;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.AdminManagement;

/// <summary>
/// Counts only. Users are counted from Identity's UserRoles and Roles (canonical role names), appointments by
/// status and clinics as a total, so no patient row, name or medical column is read (FR-STF-08, FR-STF-09,
/// FR-PRV-06).
/// </summary>
public class AdminDashboardService : IAdminDashboardService
{
    private readonly MediPassDbContext _db;

    public AdminDashboardService(MediPassDbContext db)
    {
        _db = db;
    }

    public async Task<AdminDashboardDto> GetAsync(CancellationToken cancellationToken)
    {
        var roleCounts = await (
                from userRole in _db.UserRoles.AsNoTracking()
                join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                group userRole by role.Name into grouped
                select new { Role = grouped.Key, Count = grouped.Count() })
            .ToDictionaryAsync(row => row.Role!, row => row.Count, cancellationToken);

        var statusCounts = await _db.Appointments
            .AsNoTracking()
            .GroupBy(appointment => appointment.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Status, row => row.Count, cancellationToken);

        var clinics = await _db.Clinics.AsNoTracking().CountAsync(cancellationToken);

        return new AdminDashboardDto(
            new UserCountsDto(
                roleCounts.GetValueOrDefault(DefaultRoles.Patient),
                roleCounts.GetValueOrDefault(DefaultRoles.Doctor),
                roleCounts.GetValueOrDefault(DefaultRoles.Staff),
                roleCounts.GetValueOrDefault(DefaultRoles.Admin)),
            clinics,
            new AppointmentCountsDto(
                statusCounts.GetValueOrDefault(AppointmentStatus.Booked),
                statusCounts.GetValueOrDefault(AppointmentStatus.CheckedIn),
                statusCounts.GetValueOrDefault(AppointmentStatus.InVisit),
                statusCounts.GetValueOrDefault(AppointmentStatus.Completed),
                statusCounts.GetValueOrDefault(AppointmentStatus.Cancelled)));
    }
}
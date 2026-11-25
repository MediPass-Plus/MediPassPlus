using MediPassPlus.Application.Dtos.AdminManagement;

namespace MediPassPlus.Application.AdminManagement;

public interface IAdminDashboardService
{
    /// <summary>Counts users per role, clinics and appointments per status. Metadata only.</summary>
    Task<AdminDashboardDto> GetAsync(CancellationToken cancellationToken);
}
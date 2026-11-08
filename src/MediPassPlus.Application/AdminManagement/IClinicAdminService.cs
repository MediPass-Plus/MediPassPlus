using MediPassPlus.Application.Dtos.AdminManagement;

namespace MediPassPlus.Application.AdminManagement;

public interface IClinicAdminService
{
    Task<ClinicDto> CreateAsync(CreateClinicDto request, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClinicDto>> ListAsync(CancellationToken cancellationToken);

    // Returns null when no clinic has the requested id.
    Task<ClinicDto?> UpdateAsync(int id, UpdateClinicDto request, CancellationToken cancellationToken);
}
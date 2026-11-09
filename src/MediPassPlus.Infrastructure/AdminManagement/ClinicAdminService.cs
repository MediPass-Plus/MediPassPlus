using MediPassPlus.Application.AdminManagement;
using MediPassPlus.Application.Dtos.AdminManagement;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.AdminManagement;

public class ClinicAdminService : IClinicAdminService
{
    private readonly MediPassDbContext _db;

    public ClinicAdminService(MediPassDbContext db)
    {
        _db = db;
    }

    public async Task<ClinicDto> CreateAsync(CreateClinicDto request, CancellationToken cancellationToken)
    {
        var clinic = new Clinic
        {
            Name = request.Name.Trim(),
            Address = request.Address.Trim(),
            Phone = request.Phone?.Trim()
        };

        _db.Clinics.Add(clinic);
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(clinic);
    }

    public async Task<IReadOnlyList<ClinicDto>> ListAsync(CancellationToken cancellationToken)
    {
        return await _db.Clinics
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ClinicDto(c.Id, c.Name, c.Address, c.Phone))
            .ToListAsync(cancellationToken);
    }

    public async Task<ClinicDto?> UpdateAsync(int id, UpdateClinicDto request, CancellationToken cancellationToken)
    {
        var clinic = await _db.Clinics.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (clinic is null)
        {
            return null;
        }

        clinic.Name = request.Name.Trim();
        clinic.Address = request.Address.Trim();
        clinic.Phone = request.Phone?.Trim();

        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(clinic);
    }

    private static ClinicDto ToDto(Clinic clinic) =>
        new(clinic.Id, clinic.Name, clinic.Address, clinic.Phone);
}
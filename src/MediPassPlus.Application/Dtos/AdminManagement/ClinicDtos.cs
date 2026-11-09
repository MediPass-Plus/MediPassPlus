namespace MediPassPlus.Application.Dtos.AdminManagement;

public record CreateClinicDto(string Name, string Address, string? Phone);

public record UpdateClinicDto(string Name, string Address, string? Phone);

public record ClinicDto(int Id, string Name, string Address, string? Phone);
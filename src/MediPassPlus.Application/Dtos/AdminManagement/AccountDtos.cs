namespace MediPassPlus.Application.Dtos.AdminManagement;

public record CreateDoctorDto(
    string FullName,
    string Email,
    string Password,
    string Specialty,
    int ClinicId);

public record CreateStaffDto(
    string FullName,
    string Email,
    string Password,
    int ClinicId);

public record CreatedAccountDto(
    Guid UserId,
    string FullName,
    string Email,
    string Role,
    int ProfileId,
    int ClinicId);
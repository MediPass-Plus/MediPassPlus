using MediPassPlus.Application.Dtos.AdminManagement;

namespace MediPassPlus.Application.AdminManagement;

public enum CreateAccountFailure
{
    None,
    ClinicNotFound,
    EmailAlreadyUsed,
    IdentityError
}

public sealed record CreateAccountResult(
    CreatedAccountDto? Account,
    CreateAccountFailure Failure,
    IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Failure == CreateAccountFailure.None;
}

public interface IAccountAdminService
{
    Task<CreateAccountResult> CreateDoctorAsync(CreateDoctorDto request, CancellationToken cancellationToken);

    Task<CreateAccountResult> CreateStaffAsync(CreateStaffDto request, CancellationToken cancellationToken);
}
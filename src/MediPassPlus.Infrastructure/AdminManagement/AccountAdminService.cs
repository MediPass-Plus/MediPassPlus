using MediPassPlus.Application.AdminManagement;
using MediPassPlus.Application.Dtos.AdminManagement;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.StaffAdmin;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Infrastructure.AdminManagement;

public class AccountAdminService : IAccountAdminService
{
    private readonly MediPassDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountAdminService(MediPassDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public Task<CreateAccountResult> CreateDoctorAsync(CreateDoctorDto request, CancellationToken cancellationToken) =>
        CreateAsync(
            request.FullName,
            request.Email,
            request.Password,
            request.ClinicId,
            DefaultRoles.Doctor,
            (userId, clinicId) =>
            {
                var doctor = new Doctor
                {
                    UserId = userId,
                    ClinicId = clinicId,
                    Specialty = request.Specialty.Trim()
                };
                _db.Doctors.Add(doctor);
                return () => doctor.Id;
            },
            cancellationToken);

    public Task<CreateAccountResult> CreateStaffAsync(CreateStaffDto request, CancellationToken cancellationToken) =>
        CreateAsync(
            request.FullName,
            request.Email,
            request.Password,
            request.ClinicId,
            DefaultRoles.Staff,
            (userId, clinicId) =>
            {
                var staff = new ClinicStaff { UserId = userId, ClinicId = clinicId };
                _db.ClinicStaff.Add(staff);
                return () => staff.Id;
            },
            cancellationToken);

    private async Task<CreateAccountResult> CreateAsync(
        string fullName,
        string email,
        string password,
        int clinicId,
        string role,
        Func<Guid, int, Func<int>> addProfile,
        CancellationToken cancellationToken)
    {
        var clinicExists = await _db.Clinics.AnyAsync(c => c.Id == clinicId, cancellationToken);
        if (!clinicExists)
        {
            return Fail(CreateAccountFailure.ClinicNotFound, "No clinic has the requested identifier.");
        }

        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            return Fail(CreateAccountFailure.EmailAlreadyUsed, "This email is already registered.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = fullName.Trim(),
                Role = role,
                IsActive = true,
                EmailConfirmed = true
            };

            // Step 1: the login account.
            var createResult = await _userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Fail(CreateAccountFailure.IdentityError, createResult.Errors.Select(e => e.Description));
            }

            // Step 2: the role.
            var roleResult = await _userManager.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Fail(CreateAccountFailure.IdentityError, roleResult.Errors.Select(e => e.Description));
            }

            // Step 3: the profile row (Doctors or ClinicStaff).
            var getProfileId = addProfile(user.Id, clinicId);
            await _db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new CreateAccountResult(
                new CreatedAccountDto(user.Id, user.FullName, email, role, getProfileId(), clinicId),
                CreateAccountFailure.None,
                []);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static CreateAccountResult Fail(CreateAccountFailure failure, string error) =>
        new(null, failure, [error]);

    private static CreateAccountResult Fail(CreateAccountFailure failure, IEnumerable<string> errors) =>
        new(null, failure, errors.ToList());
}
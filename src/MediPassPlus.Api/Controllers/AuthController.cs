using System.Security.Claims;
using MediPassPlus.Application.Dtos.Auth;
using MediPassPlus.Application.Email;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.Records;
using MediPassPlus.Infrastructure.Extensions;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly MediPassDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
     private readonly JwtTokenService _jwt;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
     private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;

    public AuthController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, RoleManager<IdentityRole<Guid>> roleManager, JwtTokenService jwt, MediPassDbContext context, IEmailSender emailSender, IConfiguration configuration)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _jwt = jwt;
     
        _context = context;
        _emailSender = emailSender;
        _configuration = configuration;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto request)
    {
        // Validate registration data
        if (!ModelState.IsValid)
            return BadRequest("Invalid registration data.");

        // Identity resolves a role name case-insensitively through NormalizedName, so "admin", "Admin"
        // and "ADMIN" are the same role and must reach the same branch. Resolve the canonical role first
        // and switch on that; switching on the raw request value let any casing that missed the literal
        // fall into the default branch and create an active account with the Admin role (FR-ACC-09).
        var role = await _roleManager.FindByNameAsync(request.Role);

        if (role is null)
            return BadRequest(new
            {
                message = $"Role '{request.Role}' does not exist"
            });

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            Role = request.Role,
            // FR-ACC-05: Identity only counts failed attempts for accounts that allow lockout.
            LockoutEnabled = true
        };

        // Restrict the user from self-assigning the Admin role
        switch (role.NormalizedName)
        {
            case "ADMIN":
                return BadRequest(new
                {
                    message = "This role cannot be self-assigned"
                });

            case "DOCTOR":
            case "STAFF":
                user.IsActive = false;
                break;

            default:
                user.IsActive = true;
                break;
        }

        // Start a database transaction
        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            // Step 1: Create the user in the Users table
            var result = await _userManager.CreateAsync(
                user, request.Password);

            if (!result.Succeeded)
            {
                await transaction.RollbackAsync();

                return BadRequest(
                    result.Errors.Select(e => e.Description));
            }

            // Step 2: Assign the role in the UserRoles table
            var roleResult = await _userManager.AddToRoleAsync(
                user, request.Role);

            if (!roleResult.Succeeded)
            {
                await transaction.RollbackAsync();

                return BadRequest(new
                {
                    message = "Failed to assign role. Registration was cancelled.",
                    errors = roleResult.Errors.Select(e => e.Description)
                });
            }

            await AddMedicalRecordIfPatientAsync(user);

            // Both operations succeeded, commit the transaction
            await transaction.CommitAsync();

            return Ok(new
            {
                message = "Registered successfully"
            });
        }
        catch
        {
            // Rollback if any unexpected exception occurs
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Signs a user in and issues a JWT access token.
    /// </summary>
    /// <param name="request">The account email and password.</param>
    /// <returns>The access token and the account's roles, or a generic <c>401</c>.</returns>
    /// <response code="200">The credentials were valid and the account is active.</response>
    /// <response code="401">
    /// The email is unknown, the password is wrong, the account is locked out or the account is deactivated.
    /// Every one of those answers with the same body on purpose, so the endpoint never confirms which
    /// addresses or accounts exist.
    /// </response>
    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginDto request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        if (user is null)
        {
            return InvalidCredentials();
        }

        // FR-ACC-05: every wrong password increments AccessFailedCount and the fifth one locks the account
        // for the configured window; a locked account is refused before the password is even compared.
        var signInResult = await _signInManager.CheckPasswordSignInAsync(
            user, request.Password, lockoutOnFailure: true);

        // A wrong password, a lockout and a deactivated account (FR-STF-07) are indistinguishable to the
        // caller. Reporting "account is disabled" would confirm that the address is registered.
        if (!signInResult.Succeeded || !user.IsActive)
        {
            return InvalidCredentials();
        }

        var roles = await _userManager.GetRolesAsync(user);
        var token = _jwt.CreateAccessToken(user, roles);

        return Ok(new { accessToken = token, roles });
    }
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        // A deactivated account must not be able to take itself back over.
        if (user is not null && user.IsActive)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            var link = PasswordResetMessage.BuildLink(
                _configuration["App:FrontendBaseUrl"] ?? "http://localhost:4200",
                user.Email!,
                token);

            await _emailSender.SendAsync(
                user.Email!,
                "Reset your MediPass+ password",
                PasswordResetMessage.BuildBody(link, token));
        }

        // Same answer whether the account exists, is inactive, or does not exist at all.
        return Accepted(new { message = "If the email exists, a reset link has been sent." });
    }
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(ResetPasswordDto request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        // An unknown account and a bad token produce the same answer, so the endpoint does not
        // confirm which addresses are registered.
        if (user is null)
        {
            return InvalidResetToken();
        }

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);

        if (!result.Succeeded)
        {
            return InvalidResetToken(result.Errors.Select(error => error.Description).ToArray());
        }

        // Proving control of the mailbox is enough to clear a lockout: otherwise the owner would still be
        // refused for the rest of the window with a password they have just changed (FR-ACC-05).
        await _userManager.ResetAccessFailedCountAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, null);

        return Ok(new { message = "Password has been reset successfully." });
    }

    /// <summary>
    /// Builds the single <c>401</c> answer used for an unknown email, a wrong password, a lockout and a
    /// deactivated account (ADR-0010). The body must stay identical in all four cases.
    /// </summary>
    /// <returns>The <c>401</c> result.</returns>
    private ObjectResult InvalidCredentials()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Invalid email or password.",
            Detail = "Check your credentials and try again."
        };

        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

        return Unauthorized(problem);
    }

    private ObjectResult InvalidResetToken(string[]? details = null)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "The reset link is invalid or has expired.",
            Detail = "Request a new password reset email and try again."
        };

        if (details is { Length: > 0 })
        {
            problem.Extensions["errors"] = details;
        }

        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

        return BadRequest(problem);
    }

    // Endpoint to create an admin user, accessible only by existing admins

    [Authorize(Roles = "Admin")]
    [HttpPost("create-admin")]
    public async Task<IActionResult> CreateAdmin(CreateAdminDto request)
    {
        // Validate request data
        if (!ModelState.IsValid)
            return BadRequest("Invalid registration data.");

        // Check if Admin role exists
        if (!await _roleManager.RoleExistsAsync("Admin"))
            return BadRequest(new
            {
                message = "Admin role does not exist"
            });

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            Role = "Admin",
            EmailConfirmed = true,
            // FR-ACC-05: the failed-attempt counter needs lockout enabled on the account.
            LockoutEnabled = true
        };

        // Start a database transaction
        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            // Step 1 Create the Admin user in the Users table
            var result = await _userManager.CreateAsync(
                user, request.Password);

            if (!result.Succeeded)
            {
                await transaction.RollbackAsync();

                return BadRequest(
                    result.Errors.Select(e => e.Description));
            }

            // Step 2 Assign the Admin role in the UserRoles table
            var roleResult = await _userManager.AddToRoleAsync(
                user, "Admin");

            if (!roleResult.Succeeded)
            {
                await transaction.RollbackAsync();

                return BadRequest(new
                {
                    message = "Failed to assign Admin role. Admin creation was cancelled.",
                    errors = roleResult.Errors.Select(e => e.Description)
                });
            }

            // Both operations succeeded, commit the transaction
            await transaction.CommitAsync();

            return Ok(new
            {
                message = "Admin created successfully"
            });
        }
        catch
        {
            // Rollback if any unexpected exception occurs
            await transaction.RollbackAsync();
            throw;
        }
    }



    /// <summary>
    /// Creates the patient's permanent medical record in the same unit of work as registration (FR-REC-01,
    /// ADR-0005). Only the Patient role owns a record: staff, doctors and admins never reach clinical data, and the
    /// caller's transaction rolls the record back if registration fails.
    /// </summary>
    /// <param name="user">The account that was just created and given its role.</param>
    private async Task AddMedicalRecordIfPatientAsync(ApplicationUser user)
    {
        // Identity resolves a requested role through its normalized name, so "patient" or "PATIENT" is accepted and
        // the account really gets the Patient role. Ask Identity for the actual membership instead of comparing the
        // raw request string case-sensitively, otherwise the account would silently own no record (FR-REC-01).
        if (!await _userManager.IsInRoleAsync(user, DefaultRoles.Patient))
        {
            return;
        }

        _context.MedicalRecords.Add(MedicalRecord.Create(user.Id, DateTime.UtcNow));
        await _context.SaveChangesAsync();
    }






    //[HttpPost("register-Development Version")]
    //public async Task<IActionResult> RegisterDevelopment(RegisterDto request)
    //{
    //    // Validate registration data
    //    if (!ModelState.IsValid)
    //        return BadRequest("Invalid registration data.");

    //    // Check if the requested role exists
    //    if (!await _roleManager.RoleExistsAsync(request.Role))
    //        return BadRequest(new
    //        {
    //            message = $"Role '{request.Role}' does not exist"
    //        });

    //    // Create the user
    //    var user = new ApplicationUser
    //    {
    //        UserName = request.Email,
    //        Email = request.Email,
    //        FullName = request.FullName,
    //        Role = request.Role,

    //        // Enable all accounts during development
    //        IsActive = true,

    //        EmailConfirmed = true,

    //        // FR-ACC-05: the failed-attempt counter needs lockout enabled on the account.
    //        LockoutEnabled = true
    //    };

    //    // Start a database transaction
    //    await using var transaction =
    //        await _context.Database.BeginTransactionAsync();

    //    try
    //    {
    //        // Step 1 Create the user in the Users table
    //        var result = await _userManager.CreateAsync(
    //            user, request.Password);

    //        if (!result.Succeeded)
    //        {
    //            await transaction.RollbackAsync();

    //            return BadRequest(
    //                result.Errors.Select(e => e.Description));
    //        }

    //        // Step 2 Assign the requested role in the UserRoles table
    //        var roleResult = await _userManager.AddToRoleAsync(
    //            user, request.Role);

    //        if (!roleResult.Succeeded)
    //        {
    //            await transaction.RollbackAsync();

    //            return BadRequest(new
    //            {
    //                message = "Failed to assign role. Registration was cancelled.",
    //                errors = roleResult.Errors.Select(e => e.Description)
    //            });
    //        }

    //        await AddMedicalRecordIfPatientAsync(user);

    //        // Both operations succeeded, commit the transaction
    //        await transaction.CommitAsync();

    //        return Ok(new
    //        {
    //            message = "Registered successfully",
    //            userId = user.Id,
    //            role = request.Role,
    //            isActive = user.IsActive
    //        });
    //    }
    //    catch
    //    {
    //        // Rollback if any unexpected exception occurs
    //        await transaction.RollbackAsync();
    //        throw;
    //    }
    //}


}

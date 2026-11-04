using System.Security.Claims;
using MediPassPlus.Application.Dtos.Auth;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Extensions;
using MediPassPlus.Infrastructure.Persistence;
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
     private readonly JwtTokenService _jwt;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly IWebHostEnvironment _env;
    public AuthController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager, JwtTokenService jwt, IWebHostEnvironment env, MediPassDbContext context)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _jwt = jwt;
        _env = env;
        _context = context;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto request)
    {
        // Validate registration data
        if (!ModelState.IsValid)
            return BadRequest("Invalid registration data.");

        // Check if the requested role exists
        if (!await _roleManager.RoleExistsAsync(request.Role))
            return BadRequest(new
            {
                message = $"Role '{request.Role}' does not exist"
            });

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            Role = request.Role
        };

        // Restrict the user from self-assigning the Admin role
        switch (request.Role)
        {
            case "Admin":
                return BadRequest(new
                {
                    message = "This role cannot be self-assigned"
                });

            case "Doctor":
            case "Staff":
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

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        // check if the user exists and the password is correct
        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
            return Unauthorized(new { message = "Invalid email or password" });
        // check if the user is active
        if (!user.IsActive)
            return Unauthorized(new { message = "Account is disabled" });

        var roles = await _userManager.GetRolesAsync(user);
        var token = _jwt.CreateAccessToken(user, roles);

        return Ok(new { accessToken = token, roles });
    }
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        // In a real application, you would send the reset token via email to the user. 
        if (user is not null && _env.IsDevelopment())
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            return Ok(new { resetToken = token });  
        }

        return Ok(new { message = "If the email exists, a reset link has been sent." });
    }
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordDto request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return BadRequest(new { message = "Invalid request" });
        // In a real application, you would validate the token and reset the password.
        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!ModelState.IsValid)
            return BadRequest("Invalid registration data.");
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        return Ok(new { message = "Password has been reset successfully" });
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
            EmailConfirmed = true
           
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


    [HttpPost("register-Development Version")]
    public async Task<IActionResult> RegisterDevelopment(RegisterDto request)
    {
        // Validate registration data
        if (!ModelState.IsValid)
            return BadRequest("Invalid registration data.");

        // Check if the requested role exists
        if (!await _roleManager.RoleExistsAsync(request.Role))
            return BadRequest(new
            {
                message = $"Role '{request.Role}' does not exist"
            });

        // Create the user
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            Role = request.Role,

            // Enable all accounts during development
            IsActive = true,

            EmailConfirmed = true
        };

        // Start a database transaction
        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            // Step 1 Create the user in the Users table
            var result = await _userManager.CreateAsync(
                user, request.Password);

            if (!result.Succeeded)
            {
                await transaction.RollbackAsync();

                return BadRequest(
                    result.Errors.Select(e => e.Description));
            }

            // Step 2 Assign the requested role in the UserRoles table
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

            // Both operations succeeded, commit the transaction
            await transaction.CommitAsync();

            return Ok(new
            {
                message = "Registered successfully",
                userId = user.Id,
                role = request.Role,
                isActive = user.IsActive
            });
        }
        catch
        {
            // Rollback if any unexpected exception occurs
            await transaction.RollbackAsync();
            throw;
        }
    }


    

}

using MediPassPlus.Application.Dtos.Auth;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Extensions;
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
    private readonly UserManager<ApplicationUser> _userManager;
     private readonly JwtTokenService _jwt;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly IWebHostEnvironment _env;
    public AuthController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager, JwtTokenService jwt, IWebHostEnvironment env   )
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _jwt = jwt;
        _env = env;
    }
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto request)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            Role = request.Role
        };
        if (!ModelState.IsValid)
            return BadRequest("Invalid registration data.");
 

        if (!await _roleManager.RoleExistsAsync(request.Role))

            return BadRequest(new { message = $"Role '{request.Role}' does not exist" });

        //Iwant to restrict the user from self-assigning the Admin role.
        //#region
        //if (request.Role == "Admin")
        //    return BadRequest(new { message = "This role cannot be self-assigned" });
        //#endregion



        // Create the user and assign the role
        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        await _userManager.AddToRoleAsync(user, request.Role);

        return Ok(new { message = "Registered successfully" });
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
        var user = new ApplicationUser
        {
            
            Email = request.Email,
            FullName = request.FullName,
            Role = "Admin"
            
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        await _userManager.AddToRoleAsync(user, "Admin");

        return Ok(new { message = "Admin created successfully" });
    }


}

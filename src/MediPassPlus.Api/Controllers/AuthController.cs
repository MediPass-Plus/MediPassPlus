using MediPassPlus.Application.Dtos.Auth;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Extensions;
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
    public AuthController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager, JwtTokenService jwt)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _jwt = jwt;
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

        //await _userManager.AddToRoleAsync(user, "Patient");
        if (!await _roleManager.RoleExistsAsync(request.Role))
            return BadRequest(new { message = $"Role '{request.Role}' does not exist" });


       

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

}

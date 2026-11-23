using System.Security.Claims;
using MediPassPlus.Api.RateLimiting;
using MediPassPlus.Application.Auth;
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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace MediPassPlus.Api.Controllers;

/// <summary>
/// Registration, login, refresh-token, logout and password-reset endpoints (ACC-01, MP-31).
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly MediPassDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAuthTokenService _authTokens;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
     private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IAuthTokenService authTokens, RoleManager<IdentityRole<Guid>> roleManager, MediPassDbContext context, IEmailSender emailSender, IConfiguration configuration, ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _authTokens = authTokens;
        _roleManager = roleManager;
     
        _context = context;
        _emailSender = emailSender;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Registers a patient, doctor or staff account. Admin self-registration is refused.
    /// </summary>
    /// <param name="request">The account details, including the requested role.</param>
    /// <returns>A message confirming the registration.</returns>
    /// <response code="200">The account was created with the requested role.</response>
    /// <response code="400">
    /// The payload is invalid, the role does not exist or cannot be self-assigned, or Identity rejected the account.
    /// The body is the shared <see cref="ProblemDetails"/> envelope (ADR-0010).
    /// </response>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(RegisterDto request)
    {
        // The global validation filter normally answers first; this guard keeps a body that skipped it in the same
        // envelope instead of a second error shape (ADR-0010, NFR-REL-04).
        if (!ModelState.IsValid)
        {
            return AccountRefusal(
                "Invalid registration data.",
                "The registration payload does not match the contract.");
        }

        // Identity resolves a role name case-insensitively through NormalizedName, so "admin", "Admin"
        // and "ADMIN" are the same role and must reach the same branch. Resolve the canonical role first
        // and switch on that; switching on the raw request value let any casing that missed the literal
        // fall into the default branch and create an active account with the Admin role (FR-ACC-09).
        var role = await _roleManager.FindByNameAsync(request.Role);

        if (role is null)
        {
            return AccountRefusal(
                "The requested role does not exist.",
                $"Role '{request.Role}' does not exist.");
        }

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
                return AccountRefusal(
                    "This role cannot be self-assigned.",
                    "The Admin role is granted by an existing administrator, never by public registration.");

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

                return AccountRefusal(
                    "The account could not be created.",
                    Describe(result));
            }

            // Step 2: Assign the role in the UserRoles table
            var roleResult = await _userManager.AddToRoleAsync(
                user, request.Role);

            if (!roleResult.Succeeded)
            {
                await transaction.RollbackAsync();

                return AccountRefusal(
                    "The account could not be created.",
                    "Registration was cancelled because the role could not be assigned. "
                        + Describe(roleResult));
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
    /// Signs a user in, issues a JWT access token and opens a refresh-token session.
    /// </summary>
    /// <param name="request">The account email and password, plus an optional device label.</param>
    /// <returns>
    /// The access token, the single-use refresh token and their lifetimes, or a generic <c>401</c>.
    /// </returns>
    /// <response code="200">
    /// The credentials were valid and the account is active. The refresh token is returned exactly once and must be
    /// kept by the client; the server stores only its hash.
    /// </response>
    /// <response code="400">
    /// The body is missing, or the email or password fails its validation rule. The response is the shared
    /// <c>ValidationProblemDetails</c> envelope produced by the global validation filter (NFR-SEC-06).
    /// </response>
    /// <response code="401">
    /// The email is unknown, the password is wrong, the account is locked out or the account is deactivated.
    /// Every one of those answers with the same body on purpose, so the endpoint never confirms which
    /// addresses or accounts exist.
    /// </response>
    /// <response code="429">
    /// The email and client address exceeded the configured sign-in budget. The response is the shared
    /// ProblemDetails envelope with a <c>Retry-After</c> header and carries no account identifier or medical
    /// content (ADR-0007 decision 3).
    /// </response>
    [HttpPost("login")]
    [EnableRateLimiting(LoginRateLimitPolicy.Name)]
    [ProducesResponseType(typeof(AuthTokensResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
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

        PreventTokenCaching();

        return Ok(await _authTokens.IssueAsync(user, request.DeviceId, HttpContext.RequestAborted));
    }

    /// <summary>
    /// Exchanges a refresh token for a new access token and a new refresh token (FR-ACC-04, FR-ACC-06).
    ///
    /// The presented token is single use: it is revoked here and replaced. Presenting a value that was already
    /// rotated is treated as a stolen token and ends every live session of the account, so a copy that leaked
    /// cannot keep a session alive.
    /// </summary>
    /// <param name="request">The refresh token from the previous login or refresh, plus an optional device label.</param>
    /// <param name="cancellationToken">Cancels the database work when the client disconnects.</param>
    /// <returns>The new pair, or a generic <c>401</c>.</returns>
    /// <response code="200">The token was valid, still inside its lifetime and inside the session cap.</response>
    /// <response code="400">The body is missing the token.</response>
    /// <response code="401">
    /// The token is unknown, revoked, already rotated, expired, past the session cap, or belongs to a deactivated
    /// account. Every one of those answers with the same body on purpose.
    /// </response>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthTokensResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var tokens = await _authTokens.RotateAsync(
            request.RefreshToken, request.DeviceId, cancellationToken);

        if (tokens is null)
        {
            return InvalidRefreshToken();
        }

        PreventTokenCaching();

        return Ok(tokens);
    }

    /// <summary>
    /// Revokes the presented refresh token, ending its session (FR-ACC-06).
    /// </summary>
    /// <param name="request">The refresh token to revoke.</param>
    /// <param name="cancellationToken">Cancels the database work when the client disconnects.</param>
    /// <returns><c>204</c>, including when the token is unknown or was already revoked.</returns>
    /// <response code="204">
    /// The session is over. The answer is the same for an unknown or already-revoked token, so the endpoint cannot be
    /// used to discover which tokens exist.
    /// </response>
    /// <response code="400">The body is missing the token.</response>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        await _authTokens.RevokeAsync(request.RefreshToken, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Starts the password-reset flow. The answer is the same whether the email exists, so the endpoint never
    /// confirms which addresses are registered. The rate limit is per email and client address in front of the
    /// mail-sending behaviour (FR-PRV-07, ADR-0007 decision 2).
    /// </summary>
    /// <param name="request">The account email.</param>
    /// <returns><c>202</c> whether or not an email was sent.</returns>
    /// <response code="202">The request was accepted; an email is sent only for an active account.</response>
    /// <response code="400">The payload is invalid.</response>
    /// <response code="429">
    /// The email and client address exceeded the configured reset-request budget. The response is the shared
    /// ProblemDetails envelope with a <c>Retry-After</c> header and carries no account identifier (ADR-0007
    /// decision 3).
    /// </response>
    [HttpPost("forgot-password")]
    [EnableRateLimiting(PasswordResetRateLimitPolicy.RequestName)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        // The same synthetic identifier is logged on every path, so the log cannot confirm which addresses are
        // registered. The correlation id the pipeline already opened as a scope is the only identifier (FR-PRV-08).
        _logger.LogInformation("Password reset requested.");

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

    /// <summary>
    /// Completes a password reset with the emailed token and revokes every live session. The rate limit is per
    /// email and client address in front of the other anonymous paths, so a caller cannot spend another account's
    /// budget and an enumeration probe still gets the shared refusal (FR-PRV-07, ADR-0007 decision 2).
    /// </summary>
    /// <param name="request">The email, the reset token and the new password.</param>
    /// <returns>A message confirming the reset.</returns>
    /// <response code="200">The password was reset and every live session was revoked.</response>
    /// <response code="400">
    /// The request is refused and the body is a <see cref="ProblemDetails"/>. A body that breaks a validation rule is a
    /// <see cref="ValidationProblemDetails"/> whose <c>errors</c> bag names the offending member. An unknown email, an
    /// invalid, expired or already-used token and every Identity refusal share one identical body with no <c>errors</c>
    /// bag, so the endpoint cannot be used to discover which addresses are registered.
    /// </response>
    /// <response code="429">
    /// The email and client address exceeded the configured reset-submit budget. The response is the shared
    /// ProblemDetails envelope with a <c>Retry-After</c> header and carries no token, no password, no account
    /// identifier and no medical content (ADR-0007 decision 3, ADR-0010).
    /// </response>
    [HttpPost("reset-password")]
    [EnableRateLimiting(PasswordResetRateLimitPolicy.SubmitName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
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
            return InvalidResetToken();
        }

        // Ownership of the mailbox is proven at this point, so the outcome is worth a line; the correlation id of
        // this request is the only identifier it carries (FR-PRV-08).
        _logger.LogInformation("Password reset completed.");

        // Proving control of the mailbox is enough to clear a lockout: otherwise the owner would still be
        // refused for the rest of the window with a password they have just changed (FR-ACC-05).
        await _userManager.ResetAccessFailedCountAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, null);

        // A token that leaked before the reset must not outlive it: the new password is the only proof of ownership
        // from here on (FR-ACC-06).
        await _authTokens.RevokeAllForUserAsync(user.Id, HttpContext.RequestAborted);

        return Ok(new { message = "Password has been reset successfully." });
    }

    /// <summary>
    /// Marks a response that carries a token as non-cacheable (RFC 6749 section 5.1), so no proxy or browser keeps
    /// the access or refresh token. The visit-access endpoints already do the same for their secrets.
    /// </summary>
    private void PreventTokenCaching()
    {
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Pragma"] = "no-cache";
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

    /// <summary>
    /// Builds the single <c>401</c> answer used when a refresh token cannot be exchanged: unknown, already rotated,
    /// revoked, expired, past the session cap or owned by a deactivated account (ADR-0010, NFR-SEC-10). The body
    /// must stay identical in all of those cases.
    /// </summary>
    /// <returns>The <c>401</c> result.</returns>
    private ObjectResult InvalidRefreshToken()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Invalid or expired refresh token.",
            Detail = "Sign in again to start a new session."
        };

        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

        return Unauthorized(problem);
    }

    /// <summary>
    /// Builds the single <c>400</c> answer used when a reset cannot be completed: an unknown email, an invalid, expired
    /// or already-used token, or an Identity refusal. The body must stay identical in every case, so the endpoint
    /// cannot be used to discover which addresses are registered (FR-ACC-11, NFR-SEC-06).
    /// </summary>
    /// <returns>The <c>400</c> result.</returns>
    private ObjectResult InvalidResetToken()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "The reset link is invalid or has expired.",
            Detail = "Request a new password reset email and try again."
        };

        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

        return BadRequest(problem);
    }

    /// <summary>
    /// Builds the shared <c>400</c> envelope for a refused registration or admin creation (ADR-0010). The title is
    /// stable for each branch and the detail never includes a submitted password, token or stored hash.
    /// </summary>
    /// <param name="title">A short description of the refused request.</param>
    /// <param name="detail">The safe, caller-facing reason for the refusal.</param>
    /// <returns>The <c>400</c> result.</returns>
    private ObjectResult AccountRefusal(string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = title,
            Detail = detail
        };

        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

        return BadRequest(problem);
    }

    /// <summary>
    /// Joins Identity's refusal descriptions into one safe detail. Identity reports rule violations, never the
    /// submitted password or token, and the caller's identifier is already in the correlation id (FR-PRV-08).
    /// </summary>
    /// <param name="result">The failed Identity operation.</param>
    /// <returns>The descriptions separated by semicolons.</returns>
    private static string Describe(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(error => error.Description));

    // Endpoint to create an admin user, accessible only by existing admins

    /// <summary>
    /// Creates an admin account. Only an existing admin may call it.
    /// </summary>
    /// <param name="request">The new admin's email, full name and password.</param>
    /// <returns>A message confirming the creation.</returns>
    /// <response code="200">The admin account was created.</response>
    /// <response code="400">
    /// The payload is invalid, the Admin role does not exist, or Identity rejected the account. The body is the shared
    /// <see cref="ProblemDetails"/> envelope (ADR-0010).
    /// </response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="403">The caller is not an admin.</response>
    [Authorize(Roles = "Admin")]
    [HttpPost("create-admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateAdmin(CreateAdminDto request)
    {
        // Validate request data
        if (!ModelState.IsValid)
        {
            return AccountRefusal(
                "Invalid registration data.",
                "The account payload does not match the contract.");
        }

        // Check if Admin role exists
        if (!await _roleManager.RoleExistsAsync("Admin"))
        {
            return AccountRefusal(
                "The Admin role does not exist.",
                "This environment has no Admin role, so an administrator cannot be created here.");
        }

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

                return AccountRefusal(
                    "The admin account could not be created.",
                    Describe(result));
            }

            // Step 2 Assign the Admin role in the UserRoles table
            var roleResult = await _userManager.AddToRoleAsync(
                user, "Admin");

            if (!roleResult.Succeeded)
            {
                await transaction.RollbackAsync();

                return AccountRefusal(
                    "The admin account could not be created.",
                    "Admin creation was cancelled because the role could not be assigned. "
                        + Describe(roleResult));
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

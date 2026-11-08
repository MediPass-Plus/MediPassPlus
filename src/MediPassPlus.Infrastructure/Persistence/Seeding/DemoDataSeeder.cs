using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Domain.StaffAdmin;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediPassPlus.Infrastructure.Persistence.Seeding;

/// <summary>
/// Seeds the synthetic demo data used by the demo and by manual testing (MP-96): one clinic, two doctors, future
/// slots and one admin, one staff and one patient account.
/// <para>
/// It runs only from the Development startup path in <c>Program.cs</c>. It never falls back to a default password:
/// when <c>Seed:DemoPassword</c> is missing it writes a warning and creates nothing. It is idempotent, so restarting
/// the API adds no duplicate rows.
/// </para>
/// </summary>
public sealed class DemoDataSeeder : ISeeder
{
    private const string MissingPasswordMessage =
        "Demo seed data skipped: set {Section}:DemoPassword in user-secrets to create the demo accounts.";

    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<IdentityRole<Guid>> _roles;
    private readonly IOptions<DemoSeedOptions> _options;
    private readonly ILogger<DemoDataSeeder> _logger;

    /// <summary>Creates the seeder.</summary>
    /// <param name="users">Identity user manager, so demo passwords are hashed by Identity (NFR-SEC-02).</param>
    /// <param name="roles">Identity role manager, used to make the four default roles exist.</param>
    /// <param name="options">The local demo settings that hold the password.</param>
    /// <param name="logger">Logger; it never receives a password or any seeded secret.</param>
    public DemoDataSeeder(
        UserManager<ApplicationUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        IOptions<DemoSeedOptions> options,
        ILogger<DemoDataSeeder> logger)
    {
        _users = users;
        _roles = roles;
        _options = options;
        _logger = logger;
    }

    /// <summary>The demo data is the base every other seeder builds on, so it runs first.</summary>
    public int Order => 10;

    /// <summary>
    /// Creates any missing demo account, clinic, doctor profile, staff profile, admin profile and slot. Existing rows
    /// are left untouched, which keeps the seeder safe to run on every Development start.
    /// </summary>
    /// <param name="db">The current database context.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SeedAsync(MediPassDbContext db, CancellationToken ct = default)
    {
        var password = _options.Value.DemoPassword;

        if (string.IsNullOrWhiteSpace(password))
        {
            // No password means no demo accounts; a built-in default password would be a committed secret.
            _logger.LogWarning(MissingPasswordMessage, DemoSeedOptions.SectionName);
            return;
        }

        var userIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in DemoSeedData.Accounts)
        {
            userIds[account.EmailLocalPart] = await EnsureUserAsync(account, password);
        }

        var clinic = await EnsureClinicAsync(db, ct);

        var doctors = new Dictionary<string, Doctor>(StringComparer.OrdinalIgnoreCase);
        foreach (var demoDoctor in DemoSeedData.Doctors)
        {
            var userId = userIds[demoDoctor.EmailLocalPart];
            doctors[demoDoctor.EmailLocalPart] = await EnsureDoctorAsync(db, demoDoctor, userId, clinic.Id, ct);
        }

        await EnsureStaffAsync(db, userIds[DemoSeedData.StaffEmailLocalPart], clinic.Id, ct);
        await EnsureAdminAsync(db, userIds[DemoSeedData.AdminEmailLocalPart], ct);

        foreach (var doctor in doctors.Values)
        {
            await EnsureSlotsAsync(db, doctor, ct);
        }

        _logger.LogInformation(
            "Demo seed data present: {Clinic}, {Doctors} doctors, {Accounts} accounts.",
            DemoSeedData.ClinicName,
            doctors.Count,
            userIds.Count);
    }

    private async Task<Guid> EnsureUserAsync(DemoAccount account, string password)
    {
        var existing = await _users.FindByEmailAsync(account.Email);

        if (existing is not null)
        {
            // Leave the existing account untouched, but keep its role membership correct.
            await EnsureRoleAsync(existing, account.Role);
            return existing.Id;
        }

        var user = new ApplicationUser
        {
            UserName = account.Email,
            Email = account.Email,
            EmailConfirmed = true,
            FullName = account.FullName,
            Role = account.Role,
            PhoneNumber = DemoSeedData.PhoneNumber,
            IsActive = true
        };

        EnsureSucceeded(await _users.CreateAsync(user, password), $"create the demo account '{account.Email}'");
        await EnsureRoleAsync(user, account.Role);

        return user.Id;
    }

    private async Task EnsureRoleAsync(ApplicationUser user, string role)
    {
        if (!await _roles.RoleExistsAsync(role))
        {
            EnsureSucceeded(await _roles.CreateAsync(new IdentityRole<Guid>(role)), $"create the role '{role}'");
        }

        if (!await _users.IsInRoleAsync(user, role))
        {
            EnsureSucceeded(await _users.AddToRoleAsync(user, role), $"add a demo account to the role '{role}'");
        }
    }

    private static async Task<Clinic> EnsureClinicAsync(MediPassDbContext db, CancellationToken ct)
    {
        var clinic = await db.Clinics.FirstOrDefaultAsync(c => c.Name == DemoSeedData.ClinicName, ct);
        if (clinic is not null)
        {
            return clinic;
        }

        clinic = new Clinic
        {
            Name = DemoSeedData.ClinicName,
            Address = DemoSeedData.ClinicAddress,
            Phone = DemoSeedData.PhoneNumber
        };

        db.Clinics.Add(clinic);
        await db.SaveChangesAsync(ct);

        return clinic;
    }

    private static async Task<Doctor> EnsureDoctorAsync(
        MediPassDbContext db,
        DemoDoctor demo,
        Guid userId,
        int clinicId,
        CancellationToken ct)
    {
        var doctor = await db.Doctors.FirstOrDefaultAsync(d => d.UserId == userId, ct);
        if (doctor is not null)
        {
            return doctor;
        }

        doctor = new Doctor
        {
            UserId = userId,
            Specialty = demo.Specialty,
            ClinicId = clinicId
        };

        db.Doctors.Add(doctor);
        await db.SaveChangesAsync(ct);

        return doctor;
    }

    private static async Task EnsureStaffAsync(MediPassDbContext db, Guid userId, int clinicId, CancellationToken ct)
    {
        var exists = await db.ClinicStaff.AnyAsync(staff => staff.UserId == userId, ct);
        if (exists)
        {
            return;
        }

        db.ClinicStaff.Add(new ClinicStaff { UserId = userId, ClinicId = clinicId });
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureAdminAsync(MediPassDbContext db, Guid userId, CancellationToken ct)
    {
        var exists = await db.Admins.AnyAsync(admin => admin.UserId == userId, ct);
        if (exists)
        {
            return;
        }

        db.Admins.Add(new Admin { UserId = userId });
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureSlotsAsync(MediPassDbContext db, Doctor doctor, CancellationToken ct)
    {
        var existingStarts = await db.DoctorSlots
            .Where(slot => slot.DoctorId == doctor.Id)
            .Select(slot => slot.StartTimeUtc)
            .ToListAsync(ct);

        var taken = existingStarts.ToHashSet();
        var firstDay = DateTime.UtcNow.Date.AddDays(1);
        var added = 0;

        for (var day = 0; day < DemoSeedData.SlotDays; day++)
        {
            foreach (var time in DemoSeedData.SlotStartTimesUtc)
            {
                var start = firstDay.AddDays(day).Add(time);

                // The same doctor and start time identify the same slot, so a rerun adds nothing.
                if (!taken.Add(start))
                {
                    continue;
                }

                db.DoctorSlots.Add(new DoctorSlot
                {
                    DoctorId = doctor.Id,
                    StartTimeUtc = start,
                    EndTimeUtc = start.AddMinutes(DemoSeedData.SlotMinutes),
                    IsAvailable = true
                });

                added++;
            }
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (result.Succeeded)
        {
            return;
        }

        // Identity error codes only: no user data, no password and no hash reaches the message or the logs.
        var codes = string.Join(", ", result.Errors.Select(error => error.Code));
        throw new InvalidOperationException($"Demo seeding failed to {action}. Identity errors: {codes}.");
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using MediPassPlus.Domain.Identity;
using MediPassPlus.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Infrastructure.Extensions;

internal static class IdentityServiceExtensions
{               //  Identity + Roles
    internal static IServiceCollection AddIdentityServices(this IServiceCollection services)
    {
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;

            // FR-ACC-02 asks for "at least 8 characters with a letter and a digit". The team kept the
            // stricter policy (upper + lower + digit + symbol), so the requirement is still satisfied and
            // no rule is relaxed. RequireLowercase is listed explicitly because it was previously only
            // on by Identity's default, which the DTO validators did not mirror.
            options.Password.RequiredLength = 8;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;

            // FR-ACC-05: five wrong passwords lock the account for 15 minutes. The duration is confirmed
            // by the Accounts module owner (2026-11-13); ADR-0003 still records it as an assumption and
            // stays Proposed, so its wording is a follow-up, not the value the code runs with.
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;
        })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<MediPassDbContext>()
            .AddDefaultTokenProviders()
            // CheckPasswordSignInAsync — the only API that counts failures and enforces the lockout —
            // lives on SignInManager, which AddIdentityCore does not register.
            .AddSignInManager();

        return services;
    }
}

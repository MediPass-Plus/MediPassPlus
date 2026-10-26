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
            options.Password.RequiredLength = 8;
            options.Password.RequireDigit = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
        })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<MediPassDbContext>();

        return services;
    }
}

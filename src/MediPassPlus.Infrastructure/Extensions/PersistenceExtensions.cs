using System;
using System.Collections.Generic;
using System.Text;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Infrastructure.Extensions;

internal static class PersistenceExtensions
{            // Database context + Persistence  
    internal static IServiceCollection AddPersistence(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MediPassDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .AddInterceptors(new AuditLogImmutabilityInterceptor()));

        return services;
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Audit;
using MediPassPlus.Infrastructure.Persistence.Records;
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
                .AddInterceptors(new AuditLogImmutabilityInterceptor())
                // Medical record rows are append-only: no in-place update and no delete (FR-REC-09, ADR-0005).
                .AddInterceptors(new RecordImmutabilityInterceptor()));

        return services;
    }
}

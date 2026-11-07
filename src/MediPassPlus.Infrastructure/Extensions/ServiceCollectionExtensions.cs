using System;
using System.Collections.Generic;
using System.Text;
using MediPassPlus.Application.Audit;
using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Audit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // HERE we are adding OR registering all the infrastructure services to the DI container
        services.AddPersistence(configuration);
        // register Identity Serice
        services.AddIdentityServices();             
          // Authentication & JWT
     services.AddJwtAuthentication(configuration);
        services.AddScoped<JwtTokenService>();

        // Audit writes are append-only and timestamped by the server (FR-PRV-02, ADR-0004).
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IAuditWriter, AuditWriter>();

        // Visit-access scope checks: resolve the appointment/doctor from the database, never from caller-supplied ids
        // (FR-ACS-06, ADR-0011 decision 8).
        services.AddScoped<IVisitAccessScopeStore, VisitAccessScopeStore>();
        services.AddScoped<IVisitAccessScopeService, VisitAccessScopeService>();

        // Add other infrastructure services here as needed






        return services;
    }
}

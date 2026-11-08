using System;
using System.Collections.Generic;
using System.Text;
using MediPassPlus.Application.Audit;
using MediPassPlus.Application.Privacy;
using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Infrastructure.Persistence;
using MediPassPlus.Infrastructure.Persistence.Audit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MediPassPlus.Application.AdminManagement;
using MediPassPlus.Infrastructure.AdminManagement;

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

        // Start-visit: reuse the pending request and replace its live QR credential (FR-ACS-01, FR-ACS-02).
        services.AddScoped<IVisitStartStore, StartVisitStore>();
        services.AddScoped<IStartVisitService, StartVisitService>();

        // The grant gate every medical endpoint must pass: database-backed on each request (FR-PRV-01, FR-ACS-06).
        services.AddScoped<IAccessGrantStore, AccessGrantStore>();
        services.AddScoped<IAccessGrantChecker, AccessGrantChecker>();

        // Add other infrastructure services here as needed


        // Admin management: clinics (MP-29).
        services.AddScoped<IClinicAdminService, ClinicAdminService>();



        return services;
    }
}

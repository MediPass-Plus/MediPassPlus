using System;
using System.Collections.Generic;
using System.Text;
using MediPassPlus.Application.AppointmentStatusFlow;
using MediPassPlus.Application.Audit;
using MediPassPlus.Application.Booking;
using MediPassPlus.Application.Common;
using MediPassPlus.Application.DoctorSchedule;
using MediPassPlus.Application.Email;
using MediPassPlus.Application.Privacy;
using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Infrastructure.Email;
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

        // Patient decision: approve creates the grant, reject creates none; both are audited in the same unit of
        // work (FR-ACS-03, FR-ACS-04, FR-PRV-02).
        services.AddScoped<IAccessRequestDecisionStore, AccessRequestDecisionStore>();
        services.AddScoped<IAccessRequestDecisionService, AccessRequestDecisionService>();

        // The grant gate every medical endpoint must pass: database-backed on each request (FR-PRV-01, FR-ACS-06).
        services.AddScoped<IAccessGrantStore, AccessGrantStore>();
        services.AddScoped<IAccessGrantChecker, AccessGrantChecker>();

        // Booking: book, cancel and reschedule with a filtered unique index as the double-booking guard
        // (FR-BKG-03, FR-BKG-04, FR-BKG-05, FR-BKG-09, NFR-REL-03).
        services.AddScoped<IBookingStore, BookingStore>();
        services.AddScoped<IBookingService, BookingService>();

        // Appointment status flow: staff check-in and doctor completion share the domain state machine, and every
        // "today" decision uses the clinic's Cairo calendar while timestamps stay UTC (FR-BKG-08, FR-STF-03,
        // FR-ACS-08).
        services.AddSingleton<IClinicTimeZone, CairoClinicTimeZone>();
        services.AddScoped<IAppointmentStatusStore, AppointmentStatusStore>();
        services.AddScoped<IAppointmentStatusService, AppointmentStatusService>();

        // Doctor daily schedule: the doctor is resolved from the authenticated user, never from a caller-supplied id
        // (FR-BKG-07, US-10).
        services.AddScoped<IDoctorScheduleStore, DoctorScheduleStore>();
        services.AddScoped<IDoctorScheduleService, DoctorScheduleService>();

        // Add other infrastructure services here as needed

        // Admin management: clinics (MP-29).
        services.AddScoped<IClinicAdminService, ClinicAdminService>();
        services.AddScoped<IAccountAdminService, AccountAdminService>();
        services.AddScoped<IAccountStatusService, AccountStatusService>();

        // Password-reset email: real SMTP when Smtp:Host is configured, otherwise a no-op sender so the API still
        // starts on a machine without mail settings.
        if (!string.IsNullOrWhiteSpace(configuration["Smtp:Host"]))
        {
            services.Configure<SmtpSettings>(configuration.GetSection(SmtpSettings.SectionName));
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender, NoOpEmailSender>();
        }



        return services;
    }
}

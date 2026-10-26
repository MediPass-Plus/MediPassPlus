using System;
using System.Collections.Generic;
using System.Text;
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

        // Add other infrastructure services here as needed







        return services;
    }
}

using System.Reflection;
using MediPassPlus.Api.Swagger;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MediPassPlus.Api.Extensions;

/// <summary>
/// Registers Swashbuckle as the single Swagger document generator and Swagger UI as its viewer (ADR-0012).
/// </summary>
public static class SwaggerExtensions
{
    /// <summary>
    /// Adds Swagger generation, the <c>Bearer</c> security scheme and the XML comments emitted by the API build.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, so calls can be chained.</returns>
    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            // Two ClinicDto types live in different namespaces; the default bare type name collides.
            options.CustomSchemaIds(SwaggerSchemaId.For);

            options.AddSecurityDefinition(AuthorizeOperationFilter.SchemeName, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter your JWT token"
            });

            // Only [Authorize] operations get the requirement; anonymous endpoints stay unmarked.
            options.OperationFilter<AuthorizeOperationFilter>();

            var xmlPath = Path.Combine(
                AppContext.BaseDirectory,
                $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");

            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
            }
        });

        return services;
    }
}

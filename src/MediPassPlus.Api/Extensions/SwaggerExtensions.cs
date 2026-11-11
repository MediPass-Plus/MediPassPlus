using Microsoft.OpenApi;

namespace MediPassPlus.Api.Extensions;

public static class SwaggerExtensions
{
    public static IServiceCollection AddSwaggerDocumentation(
        this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            // ClinicDto exists in two namespaces. The default schema id is the type name only, so the
            // document fails with a duplicate-schemaId 500. Namespace-qualify every id instead.
            options.CustomSchemaIds(CreateSchemaId);

            // Configure JWT Bearer Authentication
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter your JWT token"
            });

            // Add JWT Security Requirement
            options.AddSecurityRequirement(document =>
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                });
        });

        return services;
    }

    
    // Builds a unique schema id from the type name and its namespace. Generic types keep their
    /// argument names (<c>Clinical_ClinicDtoPagedResult</c>) so two closed generics cannot collide.
   
    private static string CreateSchemaId(Type type)
    {
        var typeName = type.Name.Split('`')[0];

        if (!type.IsConstructedGenericType)
        {
            return $"{type.Namespace?.Split('.').LastOrDefault()}_{typeName}";
        }

        return $"{string.Concat(type.GetGenericArguments().Select(CreateSchemaId))}{typeName}";
    }
}

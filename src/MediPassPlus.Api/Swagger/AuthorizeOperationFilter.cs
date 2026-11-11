using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MediPassPlus.Api.Swagger;

/// <summary>
/// Adds the <c>Bearer</c> security requirement only to operations that carry <see cref="IAuthorizeData"/>
/// metadata, so anonymous endpoints (register, login, forgot-password) stay unmarked in the document.
/// </summary>
internal sealed class AuthorizeOperationFilter : IOperationFilter
{
    /// <summary>The name of the security scheme registered by <c>AddSwaggerDocumentation</c>.</summary>
    internal const string SchemeName = "Bearer";

    /// <summary>Applies the security requirement to the current operation when it requires authorization.</summary>
    /// <param name="operation">The operation being generated.</param>
    /// <param name="context">The Swashbuckle context for the operation.</param>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var requiresAuthorization = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Any();

        if (!requiresAuthorization)
        {
            return;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = []
        });
    }
}

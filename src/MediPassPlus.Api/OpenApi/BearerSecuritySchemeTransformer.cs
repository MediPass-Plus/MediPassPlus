using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MediPassPlus.Api.OpenApi;

/// <summary>
/// Describes the JWT bearer scheme in the generated OpenAPI document and marks every operation that
/// requires authorization, so Swagger UI can send an access token with the request.
/// </summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    private const string SchemeName = "Bearer";

    /// <summary>
    /// Registers the bearer scheme under <c>components.securitySchemes</c>.
    /// </summary>
    /// <param name="document">The document being built.</param>
    /// <param name="context">Context supplied by ASP.NET Core OpenAPI.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "MediPass+ access token issued by the login endpoint. Paste the raw token value only."
        };

        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds a security requirement to operations carrying <see cref="IAuthorizeData"/> metadata. Anonymous
    /// endpoints (register, login) stay unmarked so the document reflects the real contract.
    /// </summary>
    /// <param name="operation">The operation being built.</param>
    /// <param name="context">Context for the current operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var requiresAuthorization = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Any();

        if (!requiresAuthorization)
        {
            return Task.CompletedTask;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = []
        });

        return Task.CompletedTask;
    }
}

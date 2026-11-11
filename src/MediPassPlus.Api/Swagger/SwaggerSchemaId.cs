using System.Text;

namespace MediPassPlus.Api.Swagger;

/// <summary>
/// Builds stable, namespace-qualified schema ids for the Swagger document.
///
/// Swashbuckle defaults to the bare type name, so two DTOs with the same name in different
/// namespaces (<c>Clinical.ClinicDto</c> and <c>Dtos.AdminManagement.ClinicDto</c>) collide and the
/// document fails with a duplicate-schemaId 500. The id contains the full namespace, the declaring-type
/// chain for nested types and the generic arguments, so every closed type gets its own entry.
/// </summary>
internal static class SwaggerSchemaId
{
    /// <summary>Returns the schema id Swashbuckle uses for <paramref name="type"/>.</summary>
    /// <param name="type">The CLR type referenced by an operation or a contract.</param>
    /// <returns>A non-empty, namespace-qualified id that is unique for the type.</returns>
    public static string For(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (type.IsArray)
        {
            return $"{For(type.GetElementType()!)}Array";
        }

        if (type.IsConstructedGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = string.Join('_', type.GetGenericArguments().Select(For));
            return $"{For(definition)}Of_{arguments}";
        }

        var prefix = type.DeclaringType is not null
            ? For(type.DeclaringType)
            : Sanitize(type.Namespace);

        return $"{prefix}_{SimpleName(type)}";
    }

    private static string SimpleName(Type type) => type.Name.Split('`')[0];

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Global";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }
}

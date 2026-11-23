using System.ComponentModel.DataAnnotations;

namespace MediPassPlus.Api.Security;

/// <summary>
/// The browser origins allowed to call the API (NFR-SEC-07). Only the Angular application belongs here; the list is
/// configuration, validated at startup so a wildcard, a relative value, a path or an empty list cannot silently widen
/// or break the policy.
/// </summary>
public sealed class CorsSettings : IValidatableObject
{
    /// <summary>The configuration section that binds these options.</summary>
    public const string SectionName = "Cors";

    /// <summary>The named CORS policy applied to the pipeline.</summary>
    public const string PolicyName = "Frontend";

    /// <summary>
    /// Gets or sets the allowed origins. Defaults to the local Angular development server. Each entry is an absolute
    /// <c>http</c> or <c>https</c> origin without a path, query or fragment.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = ["http://localhost:4200"];

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var origins = AllowedOrigins ?? [];

        if (origins.Length == 0)
        {
            yield return new ValidationResult(
                "Cors:AllowedOrigins must list at least one origin.",
                [nameof(AllowedOrigins)]);
        }

        foreach (var origin in origins)
        {
            if (!IsOrigin(origin))
            {
                yield return new ValidationResult(
                    $"Cors:AllowedOrigins entry '{origin}' must be an absolute http or https origin without a path.",
                    [nameof(AllowedOrigins)]);
            }
        }
    }

    private static bool IsOrigin(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.Equals(value, uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
}

using System.ComponentModel.DataAnnotations;
using System.Net;

namespace MediPassPlus.Api.Security;

/// <summary>
/// The reverse-proxy trust policy for <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> (NFR-SEC-07). Forwarded
/// headers are disabled by default: trusting them without a proxy in front lets a client spoof its address and bypass
/// every IP-keyed rule, including the login rate limit. A deployment enables them and names the proxies it trusts.
/// </summary>
public sealed class ForwardedHeadersSettings : IValidatableObject
{
    /// <summary>The configuration section that binds these options.</summary>
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Gets or sets whether the API consumes forwarded headers. Defaults to <c>false</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets how many proxies may append to the forwarded headers. Defaults to 1.</summary>
    [Range(1, 10)]
    public int ForwardLimit { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether every upstream address is trusted. This is only safe when the API is reachable exclusively
    /// through a network that strips client-supplied forwarded headers; it is an explicit, reviewed choice.
    /// </summary>
    public bool TrustAllProxies { get; set; }

    /// <summary>
    /// Gets or sets the proxy addresses trusted to supply the headers. Required when <see cref="Enabled"/> is
    /// <c>true</c> unless <see cref="TrustAllProxies"/> is also <c>true</c>.
    /// </summary>
    public string[] KnownProxies { get; set; } = [];

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enabled)
        {
            yield break;
        }

        var proxies = KnownProxies ?? [];

        if (!TrustAllProxies && proxies.Length == 0)
        {
            yield return new ValidationResult(
                "ForwardedHeaders:KnownProxies must list at least one proxy address when "
                + "ForwardedHeaders:Enabled is true and ForwardedHeaders:TrustAllProxies is false.",
                [nameof(KnownProxies)]);
        }

        foreach (var proxy in proxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                yield return new ValidationResult(
                    $"ForwardedHeaders:KnownProxies entry '{proxy}' is not an IP address.",
                    [nameof(KnownProxies)]);
            }
        }
    }
}

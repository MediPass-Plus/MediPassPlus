using System;
using System.Collections.Generic;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace MediPassPlus.Infrastructure.Email;

/// <summary>
/// Fails closed on a half-configured SMTP transport. An empty <see cref="SmtpSettings.Host"/> deliberately selects the
/// no-op sender so a fresh clone still starts, but a host with no credentials or an unusable sender address is a
/// configuration mistake that would otherwise surface as a <c>500</c> on the first password-reset request, which is
/// exactly the difference an enumeration probe looks for (FR-ACC-11, NFR-SEC-06).
/// </summary>
/// <remarks>
/// The failure messages name settings only. They never repeat a credential value, so startup logs stay identifiers-only
/// (FR-PRV-08, AGENTS.md section 5).
/// </remarks>
public sealed class SmtpSettingsValidator : IValidateOptions<SmtpSettings>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SmtpSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // No host means the no-op sender is registered: there is nothing to reach and nothing to validate.
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (options.Port is < 1 or > 65535)
        {
            failures.Add($"Smtp:Port must be between 1 and 65535; the configured value is {options.Port}.");
        }

        if (string.IsNullOrWhiteSpace(options.UserName))
        {
            failures.Add("Smtp:UserName is required when Smtp:Host is set.");
        }

        if (string.IsNullOrWhiteSpace(options.Password))
        {
            failures.Add(
                "Smtp:Password is required when Smtp:Host is set. Supply it from user-secrets or the platform "
                + "secret store, never from appsettings*.json.");
        }

        if (!IsSingleUsableAddress(options.FromAddress))
        {
            failures.Add(
                "Smtp:FromAddress must be one plain email address (no display name, no list) when Smtp:Host is set.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsSingleUsableAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();

        // A list would make the sender pick an arbitrary recipient identity; refuse it before MailAddress does.
        if (candidate.IndexOfAny(new[] { ',', ';' }) >= 0)
        {
            return false;
        }

        return MailAddress.TryCreate(candidate, out var address)
            && string.Equals(address.Address, candidate, StringComparison.OrdinalIgnoreCase);
    }
}

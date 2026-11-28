using System;
using System.Collections.Generic;
using System.Net.Mail;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace MediPassPlus.Infrastructure.Email;

/// <summary>
/// Fails closed on a half-configured SMTP transport. An empty <see cref="SmtpSettings.Host"/> deliberately selects the
/// no-op sender in Development and Testing, so a fresh clone and CI start without mail credentials; every other
/// environment must configure a transport, because silently accepting reset requests and sending nothing is not a
/// supported deployment state. A configured host with no credentials, an unusable account or sender address, or a
/// timeout outside the supported range is a configuration mistake that would otherwise surface as a <c>500</c> on the
/// first password-reset request, which is exactly the difference an enumeration probe looks for (FR-ACC-11,
/// NFR-SEC-06).
/// </summary>
/// <remarks>
/// The failure messages name settings only. They never repeat a credential value, so startup logs stay identifiers-only
/// (FR-PRV-08, AGENTS.md section 5).
/// </remarks>
public sealed class SmtpSettingsValidator : IValidateOptions<SmtpSettings>
{
    private const string TestingEnvironment = "Testing";

    private readonly IHostEnvironment _environment;

    /// <summary>Creates the validator for the host that is starting.</summary>
    /// <param name="environment">The host environment, which decides whether an empty host is allowed.</param>
    public SmtpSettingsValidator(IHostEnvironment environment) =>
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SmtpSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // No host means the no-op sender is registered: there is nothing to reach and nothing to validate, but only
        // Development and Testing may run without a real transport.
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            return IsDevelopmentOrTesting
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(
                    "Smtp:Host is required outside Development and Testing. Configure the password-reset mail "
                    + "transport, or run the API in Development or Testing to use the no-op sender.");
        }

        var failures = new List<string>();

        if (options.Port is < 1 or > 65535)
        {
            failures.Add($"Smtp:Port must be between 1 and 65535; the configured value is {options.Port}.");
        }

        if (options.TimeoutSeconds is < 1 or > 120)
        {
            failures.Add(
                $"Smtp:TimeoutSeconds must be between 1 and 120; the configured value is {options.TimeoutSeconds}.");
        }

        if (string.IsNullOrWhiteSpace(options.UserName))
        {
            failures.Add("Smtp:UserName is required when Smtp:Host is set.");
        }
        else if (!IsSingleUsableAddress(options.UserName))
        {
            failures.Add("Smtp:UserName must be one plain email address when Smtp:Host is set.");
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

    private bool IsDevelopmentOrTesting =>
        string.Equals(_environment.EnvironmentName, Environments.Development, StringComparison.OrdinalIgnoreCase)
        || string.Equals(_environment.EnvironmentName, TestingEnvironment, StringComparison.OrdinalIgnoreCase);

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

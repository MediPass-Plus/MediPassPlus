using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;

namespace MediPassPlus.Api.RateLimiting;

/// <summary>
/// Captures the email of a body-keyed request before the rate limiter runs, so <see cref="LoginRateLimitPolicy"/> and
/// <see cref="PasswordResetRateLimitPolicy"/> can partition by email and client address (ADR-0007 decision 2,
/// FR-PRV-07). The limiter runs before MVC model binding and its partition delegate is synchronous, so the body is not
/// otherwise available at that point.
/// </summary>
/// <remarks>
/// Only the endpoints whose policy is in <see cref="RateLimitEmailKey"/> are touched: sign-in, the start of the
/// password-reset flow and its completion. The read is bounded to <see cref="MaximumBodyBytes"/> and the stream is
/// always rewound, so model binding sees the original body. When the body repeats the <c>email</c> property, the last
/// occurrence wins, exactly like System.Text.Json model binding, so the limiter and the action always agree on which
/// address is spending the budget. The email value is never logged.
/// </remarks>
public sealed class RateLimitEmailKeyMiddleware
{
    /// <summary>The largest body the middleware buffers. Every body-keyed DTO is a few hundred bytes.</summary>
    public const int MaximumBodyBytes = 8 * 1024;

    private const string EmailPropertyName = "email";

    private readonly RequestDelegate _next;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public RateLimitEmailKeyMiddleware(RequestDelegate next) => _next = next;

    /// <summary>Captures the email for a body-keyed policy, then continues the pipeline.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the rest of the pipeline completes.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (AppliesTo(context))
        {
            await CaptureEmailAsync(context);
        }

        await _next(context);
    }

    private static bool AppliesTo(HttpContext context) =>
        RateLimitEmailKey.IsBodyKeyed(
            context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);

    private static async Task CaptureEmailAsync(HttpContext context)
    {
        // A declared body larger than a DTO is not worth buffering: the limiter falls back to the IP key and MVC will
        // reject the oversized or malformed body afterwards.
        if (context.Request.ContentLength > MaximumBodyBytes)
        {
            return;
        }

        context.Request.EnableBuffering();

        try
        {
            var body = new byte[MaximumBodyBytes];
            var length = 0;

            while (length < body.Length)
            {
                var read = await context.Request.Body.ReadAsync(
                    body.AsMemory(length, body.Length - length),
                    context.RequestAborted);

                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            var email = ReadEmail(body.AsMemory(0, length));
            if (email is not null)
            {
                context.Items[RateLimitEmailKey.ItemKey] = email;
            }
        }
        finally
        {
            context.Request.Body.Position = 0;
        }
    }

    private static string? ReadEmail(ReadOnlyMemory<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // A JSON object may repeat a property name and System.Text.Json (and therefore MVC model binding) keeps
            // the last occurrence. The limiter must read the same value the action will see; taking the first
            // occurrence would let a caller pair a throwaway decoy email with the address the action actually uses
            // and get a fresh budget per request, defeating the limit (FR-PRV-07).
            string? email = null;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, EmailPropertyName, StringComparison.OrdinalIgnoreCase))
                {
                    email = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : null;
                }
            }

            return RateLimitEmailKey.Normalize(email);
        }
        catch (JsonException)
        {
            // A malformed body is not an attempt with a usable key; the IP fallback still limits it.
            return null;
        }
    }
}

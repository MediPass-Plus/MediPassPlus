namespace MediPassPlus.Api.Security;

/// <summary>
/// Adds the browser-hardening response headers that apply to every response, including the API, Swagger UI and the
/// camera helper (NFR-SEC-07). Headers are written from the response's <c>OnStarting</c> callback, so a <c>429</c>
/// written by the rate limiter and a <c>500</c> written by the exception handler keep them, and a value an endpoint
/// already set is never overwritten.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    /// <summary>
    /// The content policy. <c>'unsafe-inline'</c> is deliberate: Swagger UI and the camera helper carry inline
    /// scripts and styles, and removing it would break both tools. <c>camera=(self)</c> keeps the QR scan helper
    /// working while refusing camera access to any other origin.
    /// </summary>
    internal const string ContentSecurityPolicy =
        "default-src 'self'; "
        + "img-src 'self' data: blob:; "
        + "style-src 'self' 'unsafe-inline'; "
        + "script-src 'self' 'unsafe-inline'; "
        + "connect-src 'self'; "
        + "frame-ancestors 'none'; "
        + "base-uri 'self'; "
        + "form-action 'self'";

    private readonly RequestDelegate _next;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    /// <summary>Registers the headers and continues the pipeline.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the rest of the pipeline completes.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var response = ((HttpContext)state).Response;

            SetIfMissing(response, "X-Content-Type-Options", "nosniff");
            SetIfMissing(response, "X-Frame-Options", "DENY");
            SetIfMissing(response, "Referrer-Policy", "no-referrer");
            SetIfMissing(response, "Permissions-Policy", "camera=(self)");
            SetIfMissing(response, "Content-Security-Policy", ContentSecurityPolicy);

            return Task.CompletedTask;
        }, context);

        return _next(context);
    }

    private static void SetIfMissing(HttpResponse response, string name, string value)
    {
        if (!response.Headers.ContainsKey(name))
        {
            response.Headers[name] = value;
        }
    }
}

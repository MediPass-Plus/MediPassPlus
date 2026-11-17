namespace MediPassPlus.Api.Logging;

/// <summary>
/// Gives every request one correlation id, echoes it in the response and puts it in the log scope, so every line
/// written while the request runs can be found by that id (NFR-REL-02, NFR-REL-06).
/// </summary>
/// <remarks>
/// The incoming header is validated before it is used. An unvalidated value lets a client inject newlines or
/// arbitrarily long text into the log stream, and it would also be echoed back verbatim in a response header.
/// </remarks>
public sealed class CorrelationIdMiddleware
{
    /// <summary>The request and response header that carries the correlation id.</summary>
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>The <see cref="HttpContext.Items"/> key that holds the resolved id for the current request.</summary>
    public const string ItemKey = "CorrelationId";

    /// <summary>The longest incoming value that is accepted; longer values are replaced.</summary>
    public const int MaxLength = 64;

    private const string ScopeKey = "CorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger that owns the correlation-id scope.</param>
    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Resolves the id, stores it, echoes it and runs the rest of the pipeline inside its scope.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the rest of the pipeline completes.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Resolve(context.Request.Headers[HeaderName].ToString());

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object> { [ScopeKey] = correlationId }))
        {
            await _next(context);
        }
    }

    private static string Resolve(string? incoming)
    {
        if (!string.IsNullOrWhiteSpace(incoming)
            && incoming.Length <= MaxLength
            && incoming.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            return incoming;
        }

        // 32 hexadecimal characters: the same shape as an accepted value, so probes can rely on one format.
        return Guid.NewGuid().ToString("N");
    }
}

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Errors;

/// <summary>
/// Central handler for unhandled exceptions (NFR-REL-04). It returns a generic <see cref="ProblemDetails"/> body with
/// status <c>500</c> and never exposes the exception message or a stack trace to the caller.
/// </summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private const string UnexpectedErrorType = "https://tools.ietf.org/html/rfc9110#section-15.6.1";

    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<ApiExceptionHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiExceptionHandler"/> class.
    /// </summary>
    /// <param name="problemDetailsService">Writes the shared error envelope.</param>
    /// <param name="logger">Records the failure server-side with identifiers only.</param>
    public ApiExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<ApiExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    /// <summary>
    /// Handles the exception and writes a <c>500</c> response without leaking implementation details.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="exception">The unhandled exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the response was written.</returns>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // The response body never carries the exception message or a stack trace (AGENTS.md §5, NFR-REL-04).
        // The server-side log keeps the exception for diagnosis; it must never contain medical content.
        _logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", httpContext.TraceIdentifier);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = UnexpectedErrorType
            }
        });
    }
}

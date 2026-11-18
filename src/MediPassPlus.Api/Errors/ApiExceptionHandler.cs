using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace MediPassPlus.Api.Errors;

/// <summary>
/// Central handler for unhandled exceptions (NFR-REL-04). It returns a generic <see cref="ProblemDetails"/> body with
/// status <c>500</c> and never exposes the exception message or a stack trace to the caller.
/// </summary>
/// <remarks>
/// The server-side log follows the identifiers-only rule (FR-PRV-08). Development logs the whole exception; every
/// other environment logs its type and stack trace without the message, because an exception message can carry a
/// patient name, a connection string or a parameter value — an EF Core or SQL Server failure often does.
/// </remarks>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    private const string UnexpectedErrorType = "https://tools.ietf.org/html/rfc9110#section-15.6.1";

    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<ApiExceptionHandler> _logger;
    private readonly bool _includeExceptionDetail;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiExceptionHandler"/> class.
    /// </summary>
    /// <param name="problemDetailsService">Writes the shared error envelope.</param>
    /// <param name="logger">Records the failure server-side with identifiers only.</param>
    /// <param name="environment">Decides whether the full exception may be logged (Development only).</param>
    public ApiExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<ApiExceptionHandler> logger,
        IHostEnvironment environment)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
        _includeExceptionDetail = environment.IsDevelopment();
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
        // The server-side log keeps the failure diagnosable; it must never contain medical content (FR-PRV-08).
        if (_includeExceptionDetail)
        {
            _logger.LogError(exception, "Unhandled exception. TraceId: {TraceId}", httpContext.TraceIdentifier);
        }
        else
        {
            // Type and stack trace only: the message is the part that can carry untrusted text.
            _logger.LogError(
                "Unhandled {ExceptionType}. TraceId: {TraceId}. StackTrace: {StackTrace}",
                exception.GetType().FullName,
                httpContext.TraceIdentifier,
                exception.StackTrace);
        }

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

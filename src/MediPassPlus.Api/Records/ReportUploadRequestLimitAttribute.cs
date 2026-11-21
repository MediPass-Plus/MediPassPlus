using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Api.Records;

/// <summary>
/// Refuses a report upload whose declared body is larger than the request window before MVC binds the multipart form.
/// Without this check the form reader fails first and the automatic model-state filter answers <c>400</c>, while an
/// oversized file is a <c>413</c> (FR-REC-05, NFR-SEC-09).
/// </summary>
/// <remarks>
/// The check uses <see cref="HttpRequest.ContentLength"/>, so a client that declares its length is refused before any
/// byte is buffered. A chunked request has no declared length; the controller's form limit still bounds it, and a
/// malformed or over-limit chunked body is answered as <c>400</c> by the form reader.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ReportUploadRequestLimitAttribute : Attribute, IAsyncResourceFilter
{
    /// <inheritdoc />
    public async Task OnResourceExecutionAsync(
        ResourceExecutingContext context,
        ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.HttpContext.Request.ContentLength is > ReportUploadRequestLimits.MaxRequestBytes)
        {
            var httpContext = context.HttpContext;
            httpContext.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;

            await httpContext.RequestServices
                .GetRequiredService<IProblemDetailsService>()
                .TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status413PayloadTooLarge,
                        Type = ReportUploadRequestLimits.PayloadTooLargeType,
                        Title = "The uploaded file is too large.",
                        Detail = "The maximum size for a report is 10 MB."
                    }
                });

            context.Result = new EmptyResult();
            return;
        }

        await next();
    }
}

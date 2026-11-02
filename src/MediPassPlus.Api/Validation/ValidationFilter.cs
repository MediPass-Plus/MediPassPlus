using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MediPassPlus.Api.Validation;

/// <summary>
/// Global MVC filter that validates every action argument for which an <see cref="IValidator{T}"/> is registered and
/// short-circuits the request with a <see cref="ValidationProblemDetails"/> response when a rule fails (NFR-SEC-06).
/// Arguments without a validator are left untouched, so the filter costs nothing for endpoints that do not opt in.
/// </summary>
public sealed class ValidationFilter : IAsyncActionFilter
{
    /// <summary>
    /// Validates the action arguments and, on failure, returns <c>400</c> with the shared error envelope
    /// (NFR-REL-04); otherwise continues the pipeline.
    /// </summary>
    /// <param name="context">The executing action context.</param>
    /// <param name="next">The delegate that continues the pipeline.</param>
    /// <returns>A task that completes when validation or the rest of the pipeline finishes.</returns>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());

            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationResult = await validator.ValidateAsync(
                new ValidationContext<object>(argument),
                context.HttpContext.RequestAborted);

            foreach (var failure in validationResult.Errors)
            {
                if (!errors.TryGetValue(failure.PropertyName, out var messages))
                {
                    messages = [];
                    errors[failure.PropertyName] = messages;
                }

                messages.Add(failure.ErrorMessage);
            }
        }

        if (errors.Count > 0)
        {
            var problemDetails = new ValidationProblemDetails(
                errors.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray(), StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            };

            problemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

            context.Result = new BadRequestObjectResult(problemDetails);
            return;
        }

        await next();
    }
}

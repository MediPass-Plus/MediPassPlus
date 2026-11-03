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

        // Walk the declared parameters, not the bound values: a missing or null body would otherwise vanish from
        // ActionArguments and slip past its validator into the action.
        foreach (var parameter in context.ActionDescriptor.Parameters)
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(parameter.ParameterType);

            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var name = parameter.Name ?? string.Empty;

            if (!context.ActionArguments.TryGetValue(name, out var argument) || argument is null)
            {
                // A missing or null request must not bypass a registered validator (PR #12 review, NFR-SEC-06).
                AddError(errors, name, $"'{name}' must not be empty.");
                continue;
            }

            var validationResult = await validator.ValidateAsync(
                new ValidationContext<object>(argument),
                context.HttpContext.RequestAborted);

            foreach (var failure in validationResult.Errors)
            {
                AddError(errors, failure.PropertyName, failure.ErrorMessage);
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

    private static void AddError(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }
}

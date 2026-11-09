using System.Globalization;
using System.Security.Claims;
using MediPassPlus.Application.Privacy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MediPassPlus.Api.Authorization;

/// <summary>
/// Enforces <see cref="RequireActiveGrantAttribute"/>: resolves the authenticated doctor from the user identifier
/// and the appointment from the route value, asks <see cref="IAccessGrantChecker"/> for the active grant, and
/// short-circuits with a single generic <c>403</c> response when the check is refused. The refusal body is identical
/// for every reason, so a caller cannot tell whether the appointment exists (NFR-PRI-01).
/// </summary>
/// <remarks>
/// The filter reads only two inputs: the <c>ClaimTypes.NameIdentifier</c> claim and the integer
/// <c>appointmentId</c> route value. It runs before the global validation filter so an unauthorized caller gets
/// no validation feedback about a record it may not touch, and it denies rather than allows when the route value is
/// missing or is not an integer. A query-string or request-body value is never used to choose the appointment.
/// </remarks>
public sealed class AccessGrantFilter : IAsyncActionFilter
{
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string ForbiddenType = "https://tools.ietf.org/html/rfc9110#section-15.5.4";
    private const string AppointmentArgumentName = "appointmentId";

    private readonly IAccessGrantChecker _checker;
    private readonly ILogger<AccessGrantFilter> _logger;

    /// <summary>Initializes a new instance of the <see cref="AccessGrantFilter"/> class.</summary>
    /// <param name="checker">The database-backed grant check.</param>
    /// <param name="logger">Records wiring mistakes with identifiers only.</param>
    public AccessGrantFilter(IAccessGrantChecker checker, ILogger<AccessGrantFilter> logger)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(logger);

        _checker = checker;
        _logger = logger;
    }

    /// <summary>
    /// Runs the grant check and either writes the refusal or continues the pipeline.
    /// </summary>
    /// <param name="context">The executing action context.</param>
    /// <param name="next">The delegate that continues the pipeline.</param>
    /// <returns>A task that completes when the check or the rest of the pipeline finishes.</returns>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var userIdValue = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var doctorUserId))
        {
            context.Result = CreateProblem(
                StatusCodes.Status401Unauthorized,
                UnauthorizedType,
                "Authentication required.",
                "The access token does not identify a user.",
                context.HttpContext.TraceIdentifier);
            return;
        }

        if (!TryGetAppointmentId(context, out var appointmentId))
        {
            // A decorated action without an appointment id route value is a wiring mistake. Fail closed: never allow
            // access because the identifier the grant is scoped to is missing.
            _logger.LogError(
                "The access-grant filter is applied to {Action}, but no integer 'appointmentId' route value was supplied.",
                context.ActionDescriptor.DisplayName);

            context.Result = CreateForbidden(context.HttpContext.TraceIdentifier);
            return;
        }

        var result = await _checker.RequireActiveGrantAsync(
            doctorUserId,
            appointmentId,
            context.HttpContext.RequestAborted);

        if (!result.IsAllowed)
        {
            context.Result = CreateForbidden(context.HttpContext.TraceIdentifier);
            return;
        }

        await next();
    }

    private static bool TryGetAppointmentId(ActionExecutingContext context, out int appointmentId)
    {
        // The appointment scope must come from the route. Reading only route data means a query-string or
        // request-body 'appointmentId' can never redirect the check to another appointment.
        if (context.RouteData.Values[AppointmentArgumentName] is not { } routeValue)
        {
            appointmentId = 0;
            return false;
        }

        return int.TryParse(
            Convert.ToString(routeValue, CultureInfo.InvariantCulture),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out appointmentId);
    }

    private static ObjectResult CreateForbidden(string traceIdentifier) =>
        CreateProblem(
            StatusCodes.Status403Forbidden,
            ForbiddenType,
            "You cannot access this record.",
            "An active access grant is required for this appointment.",
            traceIdentifier);

    private static ObjectResult CreateProblem(
        int statusCode,
        string type,
        string title,
        string detail,
        string traceIdentifier)
    {
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Type = type,
            Title = title,
            Detail = detail
        };

        problemDetails.Extensions["traceId"] = traceIdentifier;

        return new ObjectResult(problemDetails) { StatusCode = statusCode };
    }
}

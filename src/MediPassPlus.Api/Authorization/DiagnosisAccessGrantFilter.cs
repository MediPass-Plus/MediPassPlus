using System.Globalization;
using System.Security.Claims;
using MediPassPlus.Api.Extensions;
using MediPassPlus.Application.Privacy;
using MediPassPlus.Application.Records;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MediPassPlus.Api.Authorization;

/// <summary>
/// Enforces <see cref="RequireActiveGrantForDiagnosisAttribute"/>: resolves the diagnosis from the route to the
/// appointment and patient that own it, then asks <see cref="IAccessGrantChecker"/> for the active grant of the
/// authenticated doctor. Any refusal, including an unknown diagnosis, returns the same generic <c>403</c> so a caller
/// cannot learn whether another patient's diagnosis exists (NFR-PRI-01).
/// </summary>
/// <remarks>
/// The filter reads only the <c>ClaimTypes.NameIdentifier</c> claim and the GUID <c>diagnosisId</c> route value. It
/// runs before the global validation filter, so an unauthorized caller gets no validation feedback about a record it
/// may not touch.
/// </remarks>
public sealed class DiagnosisAccessGrantFilter : IAsyncActionFilter
{
    private const string DiagnosisArgumentName = "diagnosisId";

    private readonly IDiagnosisScopeReader _scopeReader;
    private readonly IAccessGrantChecker _checker;
    private readonly ILogger<DiagnosisAccessGrantFilter> _logger;

    /// <summary>Initializes a new instance of the <see cref="DiagnosisAccessGrantFilter"/> class.</summary>
    /// <param name="scopeReader">Resolves the stored diagnosis to its appointment and patient.</param>
    /// <param name="checker">The database-backed grant check.</param>
    /// <param name="logger">Records wiring mistakes and scope breaches with identifiers only.</param>
    public DiagnosisAccessGrantFilter(
        IDiagnosisScopeReader scopeReader,
        IAccessGrantChecker checker,
        ILogger<DiagnosisAccessGrantFilter> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeReader);
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeReader = scopeReader;
        _checker = checker;
        _logger = logger;
    }

    /// <summary>
    /// Runs the scope and grant checks and either writes the refusal or continues the pipeline.
    /// </summary>
    /// <param name="context">The executing action context.</param>
    /// <param name="next">The delegate that continues the pipeline.</param>
    /// <returns>A task that completes when the check or the rest of the pipeline finishes.</returns>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var userIdValue = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var doctorUserId))
        {
            context.Result = AccessGrantProblem.Unauthorized(context.HttpContext.TraceIdentifier);
            return;
        }

        if (!TryGetDiagnosisId(context, out var diagnosisId))
        {
            // A decorated action without a diagnosis id route value is a wiring mistake. Fail closed: never allow
            // access because the identifier the grant is scoped to is missing.
            _logger.LogError(
                "The diagnosis access-grant filter is applied to {Action}, but no GUID 'diagnosisId' route value was supplied.",
                context.ActionDescriptor.DisplayName);

            context.Result = AccessGrantProblem.Forbidden(context.HttpContext.TraceIdentifier);
            return;
        }

        var scope = await _scopeReader.FindAsync(diagnosisId, context.HttpContext.RequestAborted);
        if (scope is null)
        {
            context.Result = AccessGrantProblem.Forbidden(context.HttpContext.TraceIdentifier);
            return;
        }

        var result = await _checker.RequireActiveGrantAsync(
            doctorUserId,
            scope.AppointmentId,
            context.HttpContext.RequestAborted);

        if (!result.IsAllowed)
        {
            context.Result = AccessGrantProblem.Forbidden(context.HttpContext.TraceIdentifier);
            return;
        }

        var grant = result.Grant!;
        if (grant.AppointmentId != scope.AppointmentId || grant.PatientId != scope.PatientId)
        {
            // A grant that does not describe the diagnosis' visit is a scope breach. It is refused with the same
            // generic body and logged with identifiers only.
            _logger.LogWarning(
                "Diagnosis {DiagnosisId} is outside the verified access grant's appointment or patient scope.",
                diagnosisId);

            context.Result = AccessGrantProblem.Forbidden(context.HttpContext.TraceIdentifier);
            return;
        }

        context.HttpContext.SetVerifiedAccessGrant(grant);

        await next();
    }

    private static bool TryGetDiagnosisId(ActionExecutingContext context, out Guid diagnosisId)
    {
        // The diagnosis scope must come from the route. Reading only route data means a query-string or request-body
        // 'diagnosisId' can never redirect the check to another diagnosis.
        if (context.RouteData.Values[DiagnosisArgumentName] is not { } routeValue)
        {
            diagnosisId = Guid.Empty;
            return false;
        }

        return Guid.TryParse(
                Convert.ToString(routeValue, CultureInfo.InvariantCulture),
                out diagnosisId)
            && diagnosisId != Guid.Empty;
    }
}

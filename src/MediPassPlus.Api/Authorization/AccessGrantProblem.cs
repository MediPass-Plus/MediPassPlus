using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.Authorization;

/// <summary>
/// Builds the single refusal body shared by every access-grant filter. The body is identical for every refusal
/// reason, so a caller cannot tell whether the target exists (NFR-PRI-01).
/// </summary>
internal static class AccessGrantProblem
{
    private const string UnauthorizedType = "https://tools.ietf.org/html/rfc9110#section-15.5.2";
    private const string ForbiddenType = "https://tools.ietf.org/html/rfc9110#section-15.5.4";

    /// <summary>Creates the <c>401</c> response for a token that does not identify a user.</summary>
    /// <param name="traceIdentifier">The request trace identifier.</param>
    /// <returns>The problem response.</returns>
    public static ObjectResult Unauthorized(string traceIdentifier) =>
        Create(
            StatusCodes.Status401Unauthorized,
            UnauthorizedType,
            "Authentication required.",
            "The access token does not identify a user.",
            traceIdentifier);

    /// <summary>Creates the generic <c>403</c> response for any refused grant check.</summary>
    /// <param name="traceIdentifier">The request trace identifier.</param>
    /// <returns>The problem response.</returns>
    public static ObjectResult Forbidden(string traceIdentifier) =>
        Create(
            StatusCodes.Status403Forbidden,
            ForbiddenType,
            "You cannot access this record.",
            "An active access grant is required for this appointment.",
            traceIdentifier);

    private static ObjectResult Create(
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

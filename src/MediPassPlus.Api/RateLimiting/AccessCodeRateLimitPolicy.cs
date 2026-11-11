using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace MediPassPlus.Api.RateLimiting;

/// <summary>
/// Builds the partitions for the two six-digit-code policies (ADR-0007 decisions 2 and 4). <c>CodeGeneration</c> is
/// keyed by the authenticated patient; <c>CodeVerification</c> is keyed by the authenticated doctor account together
/// with the appointment, so one doctor cannot exhaust another doctor's attempts. The JWT carries the doctor's user
/// account id, not the <c>Doctor.Id</c>, and the appointment id disambiguates the partition.
/// </summary>
public static class AccessCodeRateLimitPolicy
{
    /// <summary>The policy name for generating a code.</summary>
    public const string GenerationName = "CodeGeneration";

    /// <summary>The policy name for verifying a code.</summary>
    public const string VerificationName = "CodeVerification";

    private const string PatientPartitionPrefix = "patient:";
    private const string DoctorPartitionPrefix = "doctor:";
    private const string AppointmentPartitionPrefix = "appointment:";
    private const string IpPartitionPrefix = "ip:";
    private const string UnknownPartitionValue = "unknown";

    /// <summary>Creates the per-patient fixed-window partition for code generation.</summary>
    /// <param name="context">The current HTTP request, including the authenticated principal.</param>
    /// <param name="options">The configured permit count and window length.</param>
    /// <returns>The partition the partitioned limiter caches for this key.</returns>
    public static RateLimitPartition<string> CreateGenerationPartition(
        HttpContext context,
        CodeGenerationRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var patientId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var partitionKey = !string.IsNullOrWhiteSpace(patientId)
            ? string.Concat(PatientPartitionPrefix, patientId)
            : CreateIpPartitionKey(context);

        return CreateFixedWindow(partitionKey, options.PermitLimit, options.WindowMinutes);
    }

    /// <summary>Creates the per-doctor-and-appointment fixed-window partition for code verification.</summary>
    /// <param name="context">The current HTTP request, including the authenticated principal.</param>
    /// <param name="options">The configured permit count and window length.</param>
    /// <returns>The partition the partitioned limiter caches for this key.</returns>
    public static RateLimitPartition<string> CreateVerificationPartition(
        HttpContext context,
        CodeVerificationRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var doctorId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var partitionKey = !string.IsNullOrWhiteSpace(doctorId)
            ? string.Concat(
                DoctorPartitionPrefix,
                doctorId,
                ":",
                AppointmentPartitionPrefix,
                context.Request.RouteValues["appointmentId"]?.ToString() ?? UnknownPartitionValue)
            : CreateIpPartitionKey(context);

        return CreateFixedWindow(partitionKey, options.PermitLimit, options.WindowMinutes);
    }

    /// <summary>Writes the generation policy's <c>429</c> response.</summary>
    /// <param name="httpContext">The request being rejected.</param>
    /// <param name="lease">The failed lease.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the response is written.</returns>
    public static ValueTask WriteGenerationRejectionAsync(
        HttpContext httpContext,
        RateLimitLease lease,
        CancellationToken cancellationToken) =>
        RateLimitProblem.WriteAsync(httpContext, lease, "Too many access-code requests.", cancellationToken);

    /// <summary>Writes the verification policy's <c>429</c> response.</summary>
    /// <param name="httpContext">The request being rejected.</param>
    /// <param name="lease">The failed lease.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the response is written.</returns>
    public static ValueTask WriteVerificationRejectionAsync(
        HttpContext httpContext,
        RateLimitLease lease,
        CancellationToken cancellationToken) =>
        RateLimitProblem.WriteAsync(httpContext, lease, "Too many access-code attempts.", cancellationToken);

    private static RateLimitPartition<string> CreateFixedWindow(
        string partitionKey,
        int permitLimit,
        int windowMinutes) =>
        RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(windowMinutes),
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    private static string CreateIpPartitionKey(HttpContext context) =>
        string.Concat(IpPartitionPrefix, context.Connection.RemoteIpAddress?.ToString() ?? UnknownPartitionValue);
}

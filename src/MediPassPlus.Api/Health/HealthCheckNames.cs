namespace MediPassPlus.Api.Health;

/// <summary>
/// The names <c>GET /health</c> reports. They are part of the endpoint contract used by probes and by the tests, so
/// they are constants instead of literals repeated in the wiring and the response writer (NFR-REL-06).
/// </summary>
public static class HealthCheckNames
{
    /// <summary>The process answered the request.</summary>
    public const string Api = "api";

    /// <summary>The API opened a connection to the configured database.</summary>
    public const string Database = "database";
}

using System.Collections.Concurrent;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// Records the graph each isolation test class owns. The collection shares one database (ADR-0006), so a class can
/// only prove cross-class isolation against rows another class actually created; this ledger is that hand-off.
/// Tests in the collection run serially, so entries are written and read without interleaving.
/// </summary>
internal static class IsolationLedger
{
    private static readonly ConcurrentDictionary<Type, BookingScenario> Scenarios = new();

    /// <summary>Records the graph a test class owns.</summary>
    /// <param name="owner">The concrete test class.</param>
    /// <param name="scenario">The graph that class acts through.</param>
    public static void Register(Type owner, BookingScenario scenario) => Scenarios[owner] = scenario;

    /// <summary>Returns a graph owned by another class, or the fallback when no other class has run yet.</summary>
    /// <param name="caller">The class asking for foreign data.</param>
    /// <param name="fallback">The graph to use when this class runs first.</param>
    /// <returns>A graph this class does not own.</returns>
    public static BookingScenario ForeignOr(Type caller, BookingScenario fallback) =>
        Scenarios.Where(entry => entry.Key != caller)
            .Select(entry => entry.Value)
            .FirstOrDefault() ?? fallback;
}

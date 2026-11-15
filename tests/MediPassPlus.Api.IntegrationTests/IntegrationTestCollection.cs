namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// Serializes the HTTP integration test classes. Each class owns its own <see cref="MediPassApiFactory"/> class
/// fixture, so this collection shares no database (ADR-0006 decision 3); it exists so the fixed-window rate limiter
/// and the process-wide test configuration stay deterministic while the test run executes one class at a time.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection
{
    /// <summary>The collection name every HTTP integration test class uses.</summary>
    public const string Name = "api-integration";
}

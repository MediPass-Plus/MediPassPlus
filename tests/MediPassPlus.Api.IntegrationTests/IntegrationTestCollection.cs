namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// Shares one API host and one isolated SQL Server database across the HTTP integration tests. Tests in this
/// collection run serially, which keeps the fixed-window rate limiter deterministic.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<MediPassApiFactory>
{
    /// <summary>The collection name every HTTP integration test class uses.</summary>
    public const string Name = "api-integration";
}

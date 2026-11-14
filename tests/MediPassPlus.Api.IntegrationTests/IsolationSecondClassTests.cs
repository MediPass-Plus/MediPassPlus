namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// The second class of the cross-class isolation proof: it seeds its own graph and must be refused on every row the
/// first class created (ADR-0006, NFR-SEC-10).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class IsolationSecondClassTests : IsolationTestBase
{
    /// <summary>Initializes the class with the shared API factory.</summary>
    /// <param name="factory">The collection fixture that hosts the API and database.</param>
    public IsolationSecondClassTests(MediPassApiFactory factory) : base(factory)
    {
    }
}

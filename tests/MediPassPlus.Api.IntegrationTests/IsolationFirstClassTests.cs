namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// One of two classes that share the collection database and prove neither can read the other's appointment data
/// (ADR-0006, NFR-SEC-10). Two real classes are used, not two methods, because the isolation being tested is the one
/// between test classes.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class IsolationFirstClassTests : IsolationTestBase
{
    /// <summary>Initializes the class with the shared API factory.</summary>
    /// <param name="factory">The collection fixture that hosts the API and database.</param>
    public IsolationFirstClassTests(MediPassApiFactory factory) : base(factory)
    {
    }
}

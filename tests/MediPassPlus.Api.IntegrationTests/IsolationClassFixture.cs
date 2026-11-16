using MediPassPlus.Domain.Clinical;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// Owns everything one isolation test class needs: its own database, its API host, the appointment graph it seeds and
/// a marker row nothing else can see. xUnit creates one fixture instance per test class and keeps it for every test
/// method of that class, so the graph is seeded once per class and never per test (ADR-0006 decision 3).
/// </summary>
public abstract class IsolationClassFixtureBase : IAsyncLifetime
{
    /// <summary>
    /// The prefix of the marker row every isolation class seeds. A class's catalog must contain its own marker and no
    /// other class's marker, which fails if two classes are ever pointed at one database.
    /// </summary>
    public const string MarkerPrefix = "isolation-marker:";

    private readonly string _className;

    /// <summary>Creates the class's factory, which owns a unique catalog named after the class.</summary>
    /// <param name="className">The concrete test class name.</param>
    protected IsolationClassFixtureBase(string className)
    {
        _className = className;
        Factory = new MediPassApiFactory(className);
    }

    /// <summary>This class's API host and database.</summary>
    public MediPassApiFactory Factory { get; }

    /// <summary>The marker row this class seeds; its name is unique to the class.</summary>
    public string MarkerName => MarkerFor(_className);

    /// <summary>The graph this class owns, seeded once per class.</summary>
    public BookingScenario Scenario { get; private set; } = null!;

    /// <summary>Recovers the marker name another isolation class seeds.</summary>
    /// <param name="className">The other class's name.</param>
    /// <returns>The marker name that class's database owns.</returns>
    public static string MarkerFor(string className) => MarkerPrefix + className;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        Scenario = await Factory.CreateBookingScenarioAsync();

        await using var db = Factory.CreateDbContext();
        db.Clinics.Add(new Clinic { Name = MarkerName, Address = "Isolation marker" });
        await db.SaveChangesAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Factory.DisposeAsync().AsTask();
}

/// <summary>Owns the first isolation class's database, host and graph.</summary>
public sealed class IsolationFirstClassFixture : IsolationClassFixtureBase
{
    /// <summary>Creates the fixture for <see cref="IsolationFirstClassTests"/>.</summary>
    public IsolationFirstClassFixture() : base(nameof(IsolationFirstClassTests))
    {
    }
}

/// <summary>Owns the second isolation class's database, host and graph.</summary>
public sealed class IsolationSecondClassFixture : IsolationClassFixtureBase
{
    /// <summary>Creates the fixture for <see cref="IsolationSecondClassTests"/>.</summary>
    public IsolationSecondClassFixture() : base(nameof(IsolationSecondClassTests))
    {
    }
}

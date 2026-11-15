using System.Net;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// Second of two classes that prove the harness gives every test class its own database (ADR-0006 decision 3). It
/// seeds the same shape of graph in its own catalog and must not be able to resolve the first class's marker.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class IsolationSecondClassTests : IClassFixture<IsolationSecondClassFixture>
{
    private static readonly string ForeignMarker =
        IsolationClassFixtureBase.MarkerFor(nameof(IsolationFirstClassTests));

    private readonly IsolationSecondClassFixture _fixture;

    /// <summary>Initializes the class with its own fixture.</summary>
    /// <param name="fixture">The class-owned database and graph.</param>
    public IsolationSecondClassTests(IsolationSecondClassFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>The catalog carries this class's name, so it is not the first class's database.</summary>
    [Fact]
    public void DatabaseName_IdentifiesThisClass()
    {
        Assert.Contains(
            nameof(IsolationSecondClassTests),
            _fixture.Factory.DatabaseName,
            StringComparison.Ordinal);
    }

    /// <summary>The class-owned appointment is readable by the patient the class fixture seeded.</summary>
    [Fact]
    public async Task OwnAppointment_IsReadableByItsOwnPatient()
    {
        using var client = _fixture.Factory.CreateAuthenticatedClient(_fixture.Scenario.PatientUser);

        using var response = await client.GetAsync(
            $"/api/appointments/{_fixture.Scenario.Appointment.Id}/access-state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>This class's database contains exactly one isolation marker: its own.</summary>
    [Fact]
    public async Task ThisClassDatabase_ContainsOnlyItsOwnMarker()
    {
        await using var db = _fixture.Factory.CreateDbContext();

        var markers = await db.Clinics
            .Where(clinic => clinic.Name.StartsWith(IsolationClassFixtureBase.MarkerPrefix))
            .Select(clinic => clinic.Name)
            .ToListAsync();

        Assert.Equal(new[] { _fixture.MarkerName }, markers);
    }

    /// <summary>
    /// A query through this class's context returns zero rows for the marker the first class owns, so the two classes
    /// never share a database.
    /// </summary>
    [Fact]
    public async Task AnotherClasssMarker_IsAbsentFromThisClassDatabase()
    {
        await using var db = _fixture.Factory.CreateDbContext();

        var foreign = await db.Clinics.SingleOrDefaultAsync(clinic => clinic.Name == ForeignMarker);

        Assert.Null(foreign);
    }
}

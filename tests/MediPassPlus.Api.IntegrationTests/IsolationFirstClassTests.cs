using System.Net;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// First of two classes that prove the harness gives every test class its own database (ADR-0006 decision 3). Its
/// class fixture seeds one appointment graph and one marker row once; the tests assert that this class's catalog
/// holds its own marker and cannot resolve a row the second class owns.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class IsolationFirstClassTests : IClassFixture<IsolationFirstClassFixture>
{
    private static readonly string ForeignMarker =
        IsolationClassFixtureBase.MarkerFor(nameof(IsolationSecondClassTests));

    private readonly IsolationFirstClassFixture _fixture;

    /// <summary>Initializes the class with its own fixture.</summary>
    /// <param name="fixture">The class-owned database and graph.</param>
    public IsolationFirstClassTests(IsolationFirstClassFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>The catalog carries this class's name, so it is not another class's database.</summary>
    [Fact]
    public void DatabaseName_IdentifiesThisClass()
    {
        Assert.Contains(
            nameof(IsolationFirstClassTests),
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
    /// A query through this class's context returns zero rows for the marker the second class owns, so one class's
    /// rows never appear in the other class's database.
    /// </summary>
    [Fact]
    public async Task AnotherClasssMarker_IsAbsentFromThisClassDatabase()
    {
        await using var db = _fixture.Factory.CreateDbContext();

        var foreign = await db.Clinics.SingleOrDefaultAsync(clinic => clinic.Name == ForeignMarker);

        Assert.Null(foreign);
    }
}

using System.Net;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// The shared body of the cross-class isolation proof. Each concrete class seeds its own disjoint graph and then
/// asserts that an appointment created outside it is unreadable, so one class's data never widens another class's
/// scope even though both share one test database (ADR-0006, NFR-SEC-10).
/// </summary>
public abstract class IsolationTestBase : IAsyncLifetime
{
    private readonly MediPassApiFactory _factory;
    private BookingScenario? _neighbour;

    /// <summary>Initializes the base with the shared API factory.</summary>
    /// <param name="factory">The collection fixture that hosts the API and database.</param>
    protected IsolationTestBase(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The graph this class acts through.</summary>
    protected BookingScenario Own { get; private set; } = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        Own = await _factory.CreateBookingScenarioAsync();
        IsolationLedger.Register(GetType(), Own);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>The owning patient can read the access state of the class's own appointment.</summary>
    [Fact]
    public async Task OwnAppointment_IsReadableByItsOwnPatient()
    {
        using var client = _factory.CreateAuthenticatedClient(Own.PatientUser);

        using var response = await client.GetAsync(
            $"/api/appointments/{Own.Appointment.Id}/access-state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>The class's own patient cannot read an appointment created outside this class.</summary>
    [Fact]
    public async Task AnotherClasssAppointment_IsForbiddenForThisClasssPatient()
    {
        var foreign = IsolationLedger.ForeignOr(GetType(), await NeighbourAsync());
        using var client = _factory.CreateAuthenticatedClient(Own.PatientUser);

        using var response = await client.GetAsync(
            $"/api/appointments/{foreign.Appointment.Id}/access-state");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>The class's own doctor cannot read an appointment created outside this class.</summary>
    [Fact]
    public async Task AnotherClasssAppointment_IsForbiddenForThisClasssDoctor()
    {
        var foreign = IsolationLedger.ForeignOr(GetType(), await NeighbourAsync());
        using var client = _factory.CreateAuthenticatedClient(Own.DoctorUser);

        using var response = await client.GetAsync(
            $"/api/appointments/{foreign.Appointment.Id}/access-state");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Two classes never share an account, clinic, doctor or appointment.</summary>
    [Fact]
    public async Task ClassesShareNoData()
    {
        var foreign = IsolationLedger.ForeignOr(GetType(), await NeighbourAsync());

        Assert.NotEqual(Own.Clinic.Id, foreign.Clinic.Id);
        Assert.NotEqual(Own.Doctor.Id, foreign.Doctor.Id);
        Assert.NotEqual(Own.DoctorUser.Id, foreign.DoctorUser.Id);
        Assert.NotEqual(Own.PatientUser.Id, foreign.PatientUser.Id);
        Assert.NotEqual(Own.Appointment.Id, foreign.Appointment.Id);
    }

    /// <summary>
    /// Seeds the fallback graph only when no other class has run yet. Each call creates GUID-unique accounts and a
    /// clinic, so the row is real but never reachable by this class's actors.
    /// </summary>
    private async Task<BookingScenario> NeighbourAsync() =>
        _neighbour ??= await _factory.CreateBookingScenarioAsync();
}

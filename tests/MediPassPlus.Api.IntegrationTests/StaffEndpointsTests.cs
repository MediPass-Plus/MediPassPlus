using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediPassPlus.Application.Staff;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP matrix for the MP-40 staff endpoints: authorization, clinic scope, slot overlap, booking and the
/// response shape (no medical fields) (FR-STF-01..05, FR-PRV-06).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class StaffEndpointsTests
{
    private static readonly string[] ForbiddenFieldWords =
        ["diagnos", "prescription", "report", "medical", "history"];

    private readonly MediPassApiFactory _factory;

    public StaffEndpointsTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    // ---------- Authentication and roles ----------

    [Fact]
    public async Task Schedule_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/api/staff/schedule?date={Today()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(DefaultRoles.Doctor)]
    [InlineData(DefaultRoles.Patient)]
    [InlineData(DefaultRoles.Admin)]
    public async Task StaffEndpoints_WithNonStaffRole_ReturnForbidden(string role)
    {
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var schedule = await client.GetAsync($"/api/staff/schedule?date={Today()}");
        using var addSlot = await client.PostAsJsonAsync("/api/staff/doctors/1/slots", NewSlotBody(1));
        using var deleteSlot = await client.DeleteAsync("/api/staff/doctors/1/slots/1");
        using var book = await client.PostAsJsonAsync(
            "/api/staff/appointments",
            new StaffBookAppointmentDto("patient@x.test", 1, 1));

        Assert.Equal(HttpStatusCode.Forbidden, schedule.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, addSlot.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deleteSlot.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, book.StatusCode);
    }

    [Fact]
    public async Task StaffEndpoints_WhenTheStaffUserHasNoProfile_ReturnForbidden()
    {
        var user = await _factory.CreateUserAsync(DefaultRoles.Staff);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var schedule = await client.GetAsync($"/api/staff/schedule?date={Today()}");

        Assert.Equal(HttpStatusCode.Forbidden, schedule.StatusCode);
    }

    // ---------- Own clinic versus another clinic ----------

    [Fact]
    public async Task AddSlot_ForADoctorOfTheOwnClinic_ReturnsCreated()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/staff/doctors/{scenario.Doctor.Id}/slots",
            NewSlotBody(day: 20));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AddSlot_ForADoctorOfAnotherClinic_ReturnsNotFound()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/staff/doctors/{scenario.OtherClinicDoctor.Id}/slots",
            NewSlotBody(day: 21));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSlot_OfADoctorInAnotherClinic_ReturnsNotFound()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        var otherSlot = await CreateFreeSlotAsync(scenario.OtherClinicDoctor.Id, day: 22);
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.DeleteAsync(
            $"/api/staff/doctors/{scenario.OtherClinicDoctor.Id}/slots/{otherSlot.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var db = _factory.CreateDbContext();
        Assert.True(await db.DoctorSlots.AnyAsync(s => s.Id == otherSlot.Id));
    }

    [Fact]
    public async Task DeleteSlot_OfTheOwnClinic_ReturnsNoContent()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        var slot = await CreateFreeSlotAsync(scenario.Doctor.Id, day: 23);
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.DeleteAsync(
            $"/api/staff/doctors/{scenario.Doctor.Id}/slots/{slot.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Book_ForADoctorOfAnotherClinic_ReturnsNotFound()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        var otherSlot = await CreateFreeSlotAsync(scenario.OtherClinicDoctor.Id, day: 24);
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.PostAsJsonAsync(
            "/api/staff/appointments",
            new StaffBookAppointmentDto(scenario.PatientUser.Email, scenario.OtherClinicDoctor.Id, otherSlot.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Overlap ----------

    [Fact]
    public async Task AddSlot_WhenItOverlapsAnExistingSlot_ReturnsConflict()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);
        var path = $"/api/staff/doctors/{scenario.Doctor.Id}/slots";

        using var first = await client.PostAsJsonAsync(path, NewSlotBody(day: 25));
        using var second = await client.PostAsJsonAsync(path, NewSlotBody(day: 25));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task AddSlot_InThePast_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/staff/doctors/{scenario.Doctor.Id}/slots",
            new CreateStaffSlotDto(
                new DateTime(2020, 1, 1, 9, 0, 0, DateTimeKind.Utc),
                new DateTime(2020, 1, 1, 9, 30, 0, DateTimeKind.Utc)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Booking ----------

    [Fact]
    public async Task Book_ForTheOwnClinic_CreatesTheAppointment_AndASecondBookingConflicts()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        var slot = await CreateFreeSlotAsync(scenario.Doctor.Id, day: 26);
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);
        var body = new StaffBookAppointmentDto(scenario.PatientUser.Email, scenario.Doctor.Id, slot.Id);

        using var first = await client.PostAsJsonAsync("/api/staff/appointments", body);
        using var second = await client.PostAsJsonAsync("/api/staff/appointments", body);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Book_ForAnUnknownPatientEmail_ReturnsNotFound()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        var slot = await CreateFreeSlotAsync(scenario.Doctor.Id, day: 27);
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.PostAsJsonAsync(
            "/api/staff/appointments",
            new StaffBookAppointmentDto("nobody@integration.medipass.test", scenario.Doctor.Id, slot.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Schedule scope and response shape ----------

    [Fact]
    public async Task Schedule_ReturnsOnlyTheOwnClinicAppointments()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var ownClient = _factory.CreateAuthenticatedClient(scenario.StaffUser);
        using var otherClient = _factory.CreateAuthenticatedClient(scenario.OtherClinicStaffUser);
        var date = Today();

        using var own = await ownClient.GetAsync($"/api/staff/schedule?date={date}");
        using var other = await otherClient.GetAsync($"/api/staff/schedule?date={date}");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);

        var ownItems = await own.Content.ReadFromJsonAsync<List<StaffScheduleItemDto>>();
        var otherItems = await other.Content.ReadFromJsonAsync<List<StaffScheduleItemDto>>();
        Assert.NotNull(ownItems);
        Assert.NotNull(otherItems);
        Assert.Contains(ownItems, i => i.AppointmentId == scenario.Appointment.Id);
        Assert.DoesNotContain(otherItems, i => i.AppointmentId == scenario.Appointment.Id);
    }

    [Fact]
    public async Task Schedule_Response_ContainsNoMedicalFields()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.GetAsync($"/api/staff/schedule?date={Today()}");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(json);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "appointmentId", "patientName", "patientPhone", "startTimeUtc", "endTimeUtc", "doctorName", "status"
        };

        foreach (var item in document.RootElement.EnumerateArray())
        {
            foreach (var property in item.EnumerateObject())
            {
                Assert.Contains(property.Name, allowed);
                foreach (var word in ForbiddenFieldWords)
                {
                    Assert.DoesNotContain(word, property.Name, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    // ---------- Helpers ----------

    private static string Today() => DateTime.UtcNow.ToString("yyyy-MM-dd");

    private static CreateStaffSlotDto NewSlotBody(int day)
    {
        var start = new DateTime(2030, 1, 1, 9, 0, 0, DateTimeKind.Utc).AddDays(day);
        return new CreateStaffSlotDto(start, start.AddMinutes(30));
    }

    private async Task<DoctorSlot> CreateFreeSlotAsync(int doctorId, int day)
    {
        var body = NewSlotBody(day);
        await using var db = _factory.CreateDbContext();
        var slot = new DoctorSlot
        {
            DoctorId = doctorId,
            StartTimeUtc = body.StartTimeUtc,
            EndTimeUtc = body.EndTimeUtc,
            IsAvailable = true
        };
        db.DoctorSlots.Add(slot);
        await db.SaveChangesAsync();
        return slot;
    }
}
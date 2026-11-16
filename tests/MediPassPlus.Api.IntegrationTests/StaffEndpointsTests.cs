using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediPassPlus.Application.Staff;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP matrix for the MP-40 staff endpoints: authorization, clinic scope, slot overlap, booking, check-in and the
/// response shape (no medical fields) (FR-STF-01..05, FR-PRV-06).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class StaffEndpointsTests : IClassFixture<MediPassApiFactory>
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
        using var checkIn = await client.PostAsync("/api/staff/appointments/1/check-in", null);

        Assert.Equal(HttpStatusCode.Forbidden, schedule.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, addSlot.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deleteSlot.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, book.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, checkIn.StatusCode);
    }

    [Fact]
    public async Task StaffEndpoints_WhenTheStaffUserHasNoProfile_ReturnForbidden()
    {
        var user = await _factory.CreateUserAsync(DefaultRoles.Staff);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var schedule = await client.GetAsync($"/api/staff/schedule?date={Today()}");

        Assert.Equal(HttpStatusCode.Forbidden, schedule.StatusCode);
    }

    [Fact]
    public async Task SlotAndBooking_WhenTheStaffUserHasNoProfile_ReturnForbidden()
    {
        var user = await _factory.CreateUserAsync(DefaultRoles.Staff);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var add = await client.PostAsJsonAsync("/api/staff/doctors/1/slots", NewSlotBody(day: 41));
        using var delete = await client.DeleteAsync("/api/staff/doctors/1/slots/1");
        using var book = await client.PostAsJsonAsync(
            "/api/staff/appointments",
            new StaffBookAppointmentDto("a@integration.medipass.test", 1, 1));

        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, book.StatusCode);
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
    public async Task DeleteSlot_WhenTheSlotHasAnActiveAppointment_ReturnsConflict()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.DeleteAsync(
            $"/api/staff/doctors/{scenario.Doctor.Id}/slots/{scenario.Slot.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var db = _factory.CreateDbContext();
        Assert.True(await db.Appointments.AnyAsync(a => a.Id == scenario.Appointment.Id));
    }

    [Fact]
    public async Task DeleteSlot_WhenTheSlotHasOnlyACancelledAppointment_ReturnsConflict()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync(AppointmentStatus.Cancelled);
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.DeleteAsync(
            $"/api/staff/doctors/{scenario.Doctor.Id}/slots/{scenario.Slot.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var db = _factory.CreateDbContext();
        Assert.True(await db.Appointments.AnyAsync(a => a.Id == scenario.Appointment.Id));
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

    [Fact]
    public async Task Book_WithASlotOfAnotherClinic_ReturnsNotFound()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        var otherSlot = await CreateFreeSlotAsync(scenario.OtherClinicDoctor.Id, day: 43);
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.PostAsJsonAsync(
            "/api/staff/appointments",
            new StaffBookAppointmentDto(scenario.PatientUser.Email, scenario.Doctor.Id, otherSlot.Id));

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
    public async Task AddSlot_WhenCreatedConcurrently_StoresExactlyOne()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        var body = NewSlotBody(day: 40);
        var path = $"/api/staff/doctors/{scenario.Doctor.Id}/slots";

        var tasks = Enumerable.Range(0, 4).Select(async _ =>
        {
            using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);
            using var response = await client.PostAsJsonAsync(path, body);
            return response.StatusCode;
        });
        var codes = await Task.WhenAll(tasks);

        Assert.Equal(1, codes.Count(c => c == HttpStatusCode.Created));
        Assert.Equal(3, codes.Count(c => c == HttpStatusCode.Conflict));
        await using var db = _factory.CreateDbContext();
        Assert.Equal(1, await db.DoctorSlots.CountAsync(
            s => s.DoctorId == scenario.Doctor.Id && s.StartTimeUtc == body.StartTimeUtc));
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

    [Fact]
    public async Task AddSlot_WhenEndIsBeforeStart_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);
        var start = new DateTime(2030, 6, 1, 9, 0, 0, DateTimeKind.Utc);

        using var response = await client.PostAsJsonAsync(
            $"/api/staff/doctors/{scenario.Doctor.Id}/slots",
            new CreateStaffSlotDto(start, start.AddMinutes(-30)));

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

    [Fact]
    public async Task Book_WhenTheEmailBelongsToANonPatient_ReturnsNotFound()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        var slot = await CreateFreeSlotAsync(scenario.Doctor.Id, day: 42);
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.PostAsJsonAsync(
            "/api/staff/appointments",
            new StaffBookAppointmentDto(scenario.DoctorUser.Email, scenario.Doctor.Id, slot.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CheckIn_ThroughTheStaffRoute_ChecksInOwnClinicAndHidesOtherClinic()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var other = _factory.CreateAuthenticatedClient(scenario.OtherClinicStaffUser);
        using var own = _factory.CreateAuthenticatedClient(scenario.StaffUser);
        var path = $"/api/staff/appointments/{scenario.Appointment.Id}/check-in";

        using var otherResponse = await other.PostAsync(path, null);
        using var ownResponse = await own.PostAsync(path, null);

        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
    }

    // ---------- Schedule scope and response shape ----------

    [Fact]
    public async Task Schedule_WithAnInvalidDate_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.GetAsync("/api/staff/schedule?date=not-a-date");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

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
    public async Task Schedule_IncludesACairoMidnightAppointment_AndHidesCancelled()
    {
        var midnightUtc = _factory.ClinicDateAtNineUtc(_factory.ClinicToday).AddHours(-9);
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync(
            slotStartUtc: midnightUtc.AddMinutes(10));
        var cancelled = await _factory.CreateAppointmentStatusScenarioAsync(AppointmentStatus.Cancelled);
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);
        using var cancelledClient = _factory.CreateAuthenticatedClient(cancelled.StaffUser);

        using var response = await client.GetAsync($"/api/staff/schedule?date={Today()}");
        using var cancelledResponse = await cancelledClient.GetAsync($"/api/staff/schedule?date={Today()}");
        var items = await response.Content.ReadFromJsonAsync<List<StaffScheduleItemDto>>();
        var cancelledItems = await cancelledResponse.Content.ReadFromJsonAsync<List<StaffScheduleItemDto>>();

        Assert.Contains(items!, i => i.AppointmentId == scenario.Appointment.Id);
        Assert.DoesNotContain(cancelledItems!, i => i.AppointmentId == cancelled.Appointment.Id);
    }

    [Fact]
    public async Task Schedule_Response_ContainsOnlyTheApprovedFields()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.GetAsync($"/api/staff/schedule?date={Today()}");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(json);
        Assert.NotEmpty(document.RootElement.EnumerateArray());

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "appointmentId", "patientName", "patientPhone", "startTimeUtc", "status"
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

    // The schedule is requested by clinic-local (Cairo) date, never by the UTC date.
    private string Today() => _factory.ClinicToday.ToString("yyyy-MM-dd");

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
using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Application.Booking;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP matrix for booking, cancellation and rescheduling: authentication, authorization, the patient scope,
/// every 400/404/409 rule and the concurrent double-booking guarantee (FR-BKG-03, FR-BKG-04, FR-BKG-05,
/// FR-BKG-09, NFR-SEC-10, NFR-REL-03).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class BookingEndpointTests
{
    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with the shared API factory.</summary>
    /// <param name="factory">The collection fixture that hosts the API and database.</param>
    public BookingEndpointTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Book_WithoutToken_ReturnsUnauthorized()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/appointments",
            new BookAppointmentRequest { DoctorId = scenario.Doctor.Id, SlotId = scenario.FreeSlot.Id });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(DefaultRoles.Doctor)]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task Book_WithNonPatientRole_ReturnsForbidden(string role)
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await client.PostAsJsonAsync(
            "/api/appointments",
            new BookAppointmentRequest { DoctorId = scenario.Doctor.Id, SlotId = scenario.FreeSlot.Id });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Book_FreeSlot_CreatesTheAppointmentAndMarksTheSlotUnavailable()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsJsonAsync(
            "/api/appointments",
            new BookAppointmentRequest { DoctorId = scenario.Doctor.Id, SlotId = scenario.FreeSlot.Id });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var appointment = await response.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(appointment);
        Assert.Equal(scenario.PatientUser.Id, appointment.PatientId);
        Assert.Equal(scenario.Doctor.Id, appointment.DoctorId);
        Assert.Equal(scenario.FreeSlot.Id, appointment.DoctorSlotId);
        Assert.Equal(nameof(AppointmentStatus.Booked), appointment.Status);
        Assert.Equal(scenario.FreeSlot.StartTimeUtc, appointment.StartTimeUtc);

        await using var db = _factory.CreateDbContext();

        var stored = await db.Appointments.AsNoTracking()
            .SingleAsync(a => a.Id == appointment.Id);
        Assert.Equal(scenario.PatientUser.Id, stored.PatientId);
        Assert.Equal(scenario.Doctor.Id, stored.DoctorId);
        Assert.Equal(AppointmentStatus.Booked, stored.Status);

        var slot = await db.DoctorSlots.AsNoTracking()
            .SingleAsync(s => s.Id == scenario.FreeSlot.Id);
        Assert.False(slot.IsAvailable);
    }

    [Fact]
    public async Task Book_SlotWithAnActiveAppointment_ReturnsConflict()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsJsonAsync(
            "/api/appointments",
            new BookAppointmentRequest
            {
                DoctorId = scenario.Doctor.Id,
                SlotId = scenario.AppointmentSlot.Id
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
    }

    [Fact]
    public async Task Book_AnotherDoctorsSlot_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsJsonAsync(
            "/api/appointments",
            new BookAppointmentRequest
            {
                DoctorId = scenario.Doctor.Id,
                SlotId = scenario.OtherDoctorSlot.Id
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
    }

    [Fact]
    public async Task Book_SlotInThePast_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        var pastSlot = await AddSlotAsync(
            scenario.Doctor.Id,
            DateTime.UtcNow.AddHours(-1),
            isAvailable: true);

        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsJsonAsync(
            "/api/appointments",
            new BookAppointmentRequest { DoctorId = scenario.Doctor.Id, SlotId = pastSlot.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Book_InactiveDoctor_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        await SetUserActiveAsync(scenario.DoctorUser.Id, isActive: false);
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsJsonAsync(
            "/api/appointments",
            new BookAppointmentRequest { DoctorId = scenario.Doctor.Id, SlotId = scenario.FreeSlot.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Book_InactivePatient_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        await SetUserActiveAsync(scenario.PatientUser.Id, isActive: false);
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsJsonAsync(
            "/api/appointments",
            new BookAppointmentRequest { DoctorId = scenario.Doctor.Id, SlotId = scenario.FreeSlot.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Book_UnknownSlot_ReturnsNotFound()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsJsonAsync(
            "/api/appointments",
            new BookAppointmentRequest { DoctorId = scenario.Doctor.Id, SlotId = 999_999 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Book_InvalidBody_ReturnsValidationProblem()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsJsonAsync(
            "/api/appointments",
            new BookAppointmentRequest { DoctorId = 0, SlotId = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.NotEmpty(problem.Errors);
    }

    [Fact]
    public async Task Book_TwoPatientsConcurrentlyBookTheSameSlot_OnlyOneSucceeds()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        var secondPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var firstClient = _factory.CreateAuthenticatedClient(scenario.PatientUser);
        using var secondClient = _factory.CreateAuthenticatedClient(secondPatient);
        var request = new BookAppointmentRequest
        {
            DoctorId = scenario.Doctor.Id,
            SlotId = scenario.FreeSlot.Id
        };

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync("/api/appointments", request),
            secondClient.PostAsJsonAsync("/api/appointments", request));

        try
        {
            var statuses = responses.Select(response => response.StatusCode).OrderBy(status => status).ToArray();
            Assert.Equal(
                new[] { HttpStatusCode.Created, HttpStatusCode.Conflict },
                statuses);

            await using var db = _factory.CreateDbContext();
            var appointments = await db.Appointments.AsNoTracking()
                .Where(a => a.DoctorSlotId == scenario.FreeSlot.Id
                    && a.Status != AppointmentStatus.Cancelled)
                .ToListAsync();

            Assert.Single(appointments);
            Assert.Contains(
                appointments[0].PatientId,
                new[] { scenario.PatientUser.Id, secondPatient.Id });

            var createdResponse = responses.Single(
                response => response.StatusCode == HttpStatusCode.Created);
            var created = await createdResponse.Content.ReadFromJsonAsync<AppointmentDto>();
            Assert.NotNull(created);
            Assert.Equal(appointments[0].Id, created.Id);
            Assert.Equal(appointments[0].PatientId, created.PatientId);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Book_AfterTheSlotWasFreedByACancellation_SucceedsAgain()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);
        var request = new BookAppointmentRequest
        {
            DoctorId = scenario.Doctor.Id,
            SlotId = scenario.FreeSlot.Id
        };

        using var firstResponse = await client.PostAsJsonAsync("/api/appointments", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(first);

        using var cancelResponse = await client.PostAsync(
            $"/api/appointments/{first.Id}/cancel",
            content: null);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        using var secondResponse = await client.PostAsJsonAsync("/api/appointments", request);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var second = await secondResponse.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(second);
        Assert.NotEqual(first.Id, second.Id);

        await using var db = _factory.CreateDbContext();
        var active = await db.Appointments.AsNoTracking()
            .Where(a => a.DoctorSlotId == scenario.FreeSlot.Id
                && a.Status != AppointmentStatus.Cancelled)
            .ToListAsync();

        Assert.Single(active);
        Assert.Equal(second.Id, active[0].Id);
    }

    [Fact]
    public async Task Cancel_BookedAppointment_ReturnsOkAndFreesTheSlot()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/cancel",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var appointment = await response.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(appointment);
        Assert.Equal(nameof(AppointmentStatus.Cancelled), appointment.Status);
        Assert.Equal(scenario.Appointment.Id, appointment.Id);
        Assert.Equal(scenario.Doctor.Id, appointment.DoctorId);
        Assert.Equal(scenario.PatientUser.Id, appointment.PatientId);
        Assert.Equal(scenario.AppointmentSlot.Id, appointment.DoctorSlotId);

        await using var db = _factory.CreateDbContext();

        var stored = await db.Appointments.AsNoTracking()
            .SingleAsync(a => a.Id == scenario.Appointment.Id);
        Assert.Equal(AppointmentStatus.Cancelled, stored.Status);

        var slot = await db.DoctorSlots.AsNoTracking()
            .SingleAsync(s => s.Id == scenario.AppointmentSlot.Id);
        Assert.True(slot.IsAvailable);
    }

    [Fact]
    public async Task Cancel_AnotherPatientsAppointment_ReturnsNotFoundAndChangesNothing()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(otherPatient);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/cancel",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var db = _factory.CreateDbContext();

        Assert.Equal(
            AppointmentStatus.Booked,
            (await db.Appointments.AsNoTracking()
                .SingleAsync(a => a.Id == scenario.Appointment.Id)).Status);
        Assert.False(
            (await db.DoctorSlots.AsNoTracking()
                .SingleAsync(s => s.Id == scenario.AppointmentSlot.Id)).IsAvailable);
    }

    [Fact]
    public async Task Cancel_AppointmentThatIsNotBooked_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateBookingScenarioAsync(
            appointmentStatus: AppointmentStatus.CheckedIn);
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/cancel",
            content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_AppointmentThatAlreadyStarted_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateBookingScenarioAsync(
            slotStartUtc: DateTime.UtcNow.AddHours(-2));
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/cancel",
            content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_WithoutToken_ReturnsUnauthorized()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        using var client = _factory.CreateClient();

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/cancel",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(DefaultRoles.Doctor)]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task Cancel_WithNonPatientRole_ReturnsForbidden(string role)
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/cancel",
            content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reschedule_BookedAppointment_MovesItAndSwapsSlotAvailability()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Appointment.Id}/reschedule",
            new RescheduleAppointmentRequest { NewSlotId = scenario.TargetSlot.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var appointment = await response.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(appointment);
        Assert.Equal(scenario.TargetSlot.Id, appointment.DoctorSlotId);
        Assert.Equal(scenario.Doctor.Id, appointment.DoctorId);
        Assert.Equal(scenario.PatientUser.Id, appointment.PatientId);

        await using var db = _factory.CreateDbContext();

        var stored = await db.Appointments.AsNoTracking()
            .SingleAsync(a => a.Id == scenario.Appointment.Id);
        Assert.Equal(scenario.TargetSlot.Id, stored.DoctorSlotId);
        Assert.Equal(scenario.Doctor.Id, stored.DoctorId);
        Assert.Equal(scenario.PatientUser.Id, stored.PatientId);

        Assert.True(
            (await db.DoctorSlots.AsNoTracking()
                .SingleAsync(s => s.Id == scenario.AppointmentSlot.Id)).IsAvailable);
        Assert.False(
            (await db.DoctorSlots.AsNoTracking()
                .SingleAsync(s => s.Id == scenario.TargetSlot.Id)).IsAvailable);
    }

    [Fact]
    public async Task Reschedule_ToTheCurrentSlot_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Appointment.Id}/reschedule",
            new RescheduleAppointmentRequest { NewSlotId = scenario.AppointmentSlot.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reschedule_ToAnOccupiedSlot_ReturnsConflict()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        var occupiedSlot = await AddSlotAsync(
            scenario.Doctor.Id,
            scenario.TargetSlot.StartTimeUtc.AddHours(1),
            isAvailable: false);
        await AddAppointmentAsync(otherPatient.Id, scenario.Doctor.Id, occupiedSlot.Id);

        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Appointment.Id}/reschedule",
            new RescheduleAppointmentRequest { NewSlotId = occupiedSlot.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Reschedule_ToAnotherDoctorsSlot_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Appointment.Id}/reschedule",
            new RescheduleAppointmentRequest { NewSlotId = scenario.OtherDoctorSlot.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reschedule_AnotherPatientsAppointment_ReturnsNotFound()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(otherPatient);

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Appointment.Id}/reschedule",
            new RescheduleAppointmentRequest { NewSlotId = scenario.TargetSlot.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reschedule_AppointmentThatIsNotBooked_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateBookingScenarioAsync(
            appointmentStatus: AppointmentStatus.CheckedIn);
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Appointment.Id}/reschedule",
            new RescheduleAppointmentRequest { NewSlotId = scenario.TargetSlot.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reschedule_WithoutToken_ReturnsUnauthorized()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Appointment.Id}/reschedule",
            new RescheduleAppointmentRequest { NewSlotId = scenario.TargetSlot.Id });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(DefaultRoles.Doctor)]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task Reschedule_WithNonPatientRole_ReturnsForbidden(string role)
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await client.PostAsJsonAsync(
            $"/api/appointments/{scenario.Appointment.Id}/reschedule",
            new RescheduleAppointmentRequest { NewSlotId = scenario.TargetSlot.Id });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<DoctorSlot> AddSlotAsync(int doctorId, DateTime startUtc, bool isAvailable)
    {
        await using var db = _factory.CreateDbContext();

        var slot = new DoctorSlot
        {
            DoctorId = doctorId,
            StartTimeUtc = startUtc,
            EndTimeUtc = startUtc.AddMinutes(30),
            IsAvailable = isAvailable
        };

        db.DoctorSlots.Add(slot);
        await db.SaveChangesAsync();

        return slot;
    }

    private async Task AddAppointmentAsync(Guid patientId, int doctorId, int slotId)
    {
        await using var db = _factory.CreateDbContext();

        db.Appointments.Add(new Appointment
        {
            PatientId = patientId,
            DoctorId = doctorId,
            DoctorSlotId = slotId,
            Status = AppointmentStatus.Booked,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task SetUserActiveAsync(Guid userId, bool isActive)
    {
        await using var db = _factory.CreateDbContext();

        var user = await db.Users.SingleAsync(user => user.Id == userId);
        user.IsActive = isActive;
        await db.SaveChangesAsync();
    }
}

using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Application.Booking;
using MediPassPlus.Application.Common;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP matrix for the patient-facing appointment surface: authentication, the patient scope, the paged list,
/// paging limits, the ownership rule and the guarantee that a caller cannot supply another patient's identifier
/// (FR-BKG-03, FR-BKG-04, FR-BKG-05, FR-BKG-06, NFR-PRF-04, NFR-SEC-10).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class MyAppointmentsEndpointTests : IClassFixture<MediPassApiFactory>
{
    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with its own API factory and database.</summary>
    /// <param name="factory">The class fixture that hosts this class's API and database.</param>
    public MyAppointmentsEndpointTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/me/appointments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(DefaultRoles.Doctor)]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Admin)]
    public async Task Get_WithNonPatientRole_ReturnsForbidden(string role)
    {
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await client.GetAsync("/api/me/appointments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsThePatientsOwnAppointmentsWithDoctorClinicAndStatus()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.GetAsync("/api/me/appointments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedResult<PatientAppointmentDto>>();
        Assert.NotNull(page);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);

        var appointment = Assert.Single(page.Items);
        Assert.Equal(scenario.Appointment.Id, appointment.Id);
        Assert.Equal(scenario.Doctor.Id, appointment.DoctorId);
        Assert.Equal(scenario.AppointmentSlot.Id, appointment.DoctorSlotId);
        Assert.Equal(scenario.AppointmentSlot.StartTimeUtc, appointment.StartTimeUtc);
        Assert.Equal(scenario.AppointmentSlot.EndTimeUtc, appointment.EndTimeUtc);
        Assert.Equal(scenario.Clinic.Id, appointment.ClinicId);
        Assert.Equal(scenario.Clinic.Name, appointment.ClinicName);
        Assert.Equal(nameof(AppointmentStatus.Booked), appointment.Status);

        await using var db = _factory.CreateDbContext();
        var doctorName = await db.Users.AsNoTracking()
            .Where(user => user.Id == scenario.DoctorUser.Id)
            .Select(user => user.FullName)
            .SingleAsync();
        Assert.Equal(doctorName, appointment.DoctorName);
    }

    [Fact]
    public async Task Get_DoesNotIncludeAnotherPatientsAppointments()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        await SeedAppointmentsAsync(
            otherPatient.Id,
            scenario.Doctor.Id,
            count: 1,
            firstStartUtc: DateTime.UtcNow.Date.AddDays(3).AddHours(9));

        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.GetAsync("/api/me/appointments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedResult<PatientAppointmentDto>>();
        Assert.NotNull(page);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(scenario.Appointment.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task Get_IgnoresAPatientIdInTheQueryString()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        await SeedAppointmentsAsync(
            otherPatient.Id,
            scenario.Doctor.Id,
            count: 1,
            firstStartUtc: DateTime.UtcNow.Date.AddDays(3).AddHours(9));

        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.GetAsync(
            $"/api/me/appointments?patientId={otherPatient.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedResult<PatientAppointmentDto>>();
        Assert.NotNull(page);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(scenario.Appointment.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task Get_DefaultPageSizeIsTwenty()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        var scenario = await _factory.CreateBookingScenarioAsync();
        await SeedAppointmentsAsync(
            patient.Id,
            scenario.Doctor.Id,
            count: 25,
            firstStartUtc: DateTime.UtcNow.Date.AddDays(3).AddHours(8));

        using var client = _factory.CreateAuthenticatedClient(patient);

        using var response = await client.GetAsync("/api/me/appointments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedResult<PatientAppointmentDto>>();
        Assert.NotNull(page);
        Assert.Equal(25, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(20, page.Items.Count);
        Assert.True(page.Items[0].StartTimeUtc > page.Items[1].StartTimeUtc);
    }

    [Fact]
    public async Task Get_SecondPageReturnsTheRemainingRows()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        var scenario = await _factory.CreateBookingScenarioAsync();
        await SeedAppointmentsAsync(
            patient.Id,
            scenario.Doctor.Id,
            count: 25,
            firstStartUtc: DateTime.UtcNow.Date.AddDays(3).AddHours(8));

        using var client = _factory.CreateAuthenticatedClient(patient);

        using var response = await client.GetAsync("/api/me/appointments?page=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedResult<PatientAppointmentDto>>();
        Assert.NotNull(page);
        Assert.Equal(25, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(5, page.Items.Count);
    }

    [Fact]
    public async Task Get_AcceptsTheMaximumPageSize()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        var scenario = await _factory.CreateBookingScenarioAsync();
        await SeedAppointmentsAsync(
            patient.Id,
            scenario.Doctor.Id,
            count: 25,
            firstStartUtc: DateTime.UtcNow.Date.AddDays(3).AddHours(8));

        using var client = _factory.CreateAuthenticatedClient(patient);

        using var response = await client.GetAsync("/api/me/appointments?pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedResult<PatientAppointmentDto>>();
        Assert.NotNull(page);
        Assert.Equal(50, page.PageSize);
        Assert.Equal(25, page.Items.Count);
    }

    [Fact]
    public async Task Get_PageSizeAboveTheMaximum_ReturnsValidationProblem()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(patient);

        using var response = await client.GetAsync("/api/me/appointments?pageSize=51");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(nameof(PageRequest.PageSize), problem.Errors.Keys);
    }

    [Fact]
    public async Task Get_PageBelowOne_ReturnsValidationProblem()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(patient);

        using var response = await client.GetAsync("/api/me/appointments?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(nameof(PageRequest.Page), problem.Errors.Keys);
    }

    [Fact]
    public async Task Get_PageBeyondTheEnd_ReturnsAnEmptyPage()
    {
        var patient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        var scenario = await _factory.CreateBookingScenarioAsync();
        await SeedAppointmentsAsync(
            patient.Id,
            scenario.Doctor.Id,
            count: 25,
            firstStartUtc: DateTime.UtcNow.Date.AddDays(3).AddHours(8));

        using var client = _factory.CreateAuthenticatedClient(patient);

        using var response = await client.GetAsync("/api/me/appointments?page=3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedResult<PatientAppointmentDto>>();
        Assert.NotNull(page);
        Assert.Equal(25, page.TotalCount);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Book_IgnoresAPatientIdInTheBody()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var response = await client.PostAsJsonAsync(
            "/api/me/appointments",
            new
            {
                doctorId = scenario.Doctor.Id,
                slotId = scenario.FreeSlot.Id,
                patientId = otherPatient.Id
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var appointment = await response.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(appointment);
        Assert.Equal(scenario.PatientUser.Id, appointment.PatientId);
        Assert.NotEqual(otherPatient.Id, appointment.PatientId);
    }

    [Fact]
    public async Task Cancel_AnotherPatientsAppointment_IsIndistinguishableFromAMissingOne()
    {
        // The store filters the appointment by the token's patient, so a foreign id and an unknown id must produce
        // the same 404 envelope. Any difference would confirm that the appointment exists (FR-BKG-06).
        var scenario = await _factory.CreateBookingScenarioAsync();
        var otherPatient = await _factory.CreateUserAsync(DefaultRoles.Patient);
        using var client = _factory.CreateAuthenticatedClient(otherPatient);

        using var foreign = await client.PutAsync(
            $"/api/me/appointments/{scenario.Appointment.Id}/cancel",
            content: null);
        using var missing = await client.PutAsync(
            $"/api/me/appointments/{int.MaxValue}/cancel",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var foreignProblem = await foreign.Content.ReadFromJsonAsync<ProblemDetails>();
        var missingProblem = await missing.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(foreignProblem);
        Assert.NotNull(missingProblem);
        Assert.Equal(missingProblem.Status, foreignProblem.Status);
        Assert.Equal(missingProblem.Title, foreignProblem.Title);
        Assert.Equal(missingProblem.Detail, foreignProblem.Detail);
        Assert.Equal(missingProblem.Type, foreignProblem.Type);
    }

    [Fact]
    public async Task Book_ThenList_ShowsTheAppointment_ThenCancel_ShowsCancelled()
    {
        var scenario = await _factory.CreateBookingScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var bookResponse = await client.PostAsJsonAsync(
            "/api/me/appointments",
            new BookAppointmentRequest { DoctorId = scenario.Doctor.Id, SlotId = scenario.FreeSlot.Id });
        Assert.Equal(HttpStatusCode.Created, bookResponse.StatusCode);

        var booked = await bookResponse.Content.ReadFromJsonAsync<AppointmentDto>();
        Assert.NotNull(booked);

        var afterBooking = await ReadPageAsync(client);
        var listed = Assert.Single(afterBooking.Items, item => item.Id == booked.Id);
        Assert.Equal(nameof(AppointmentStatus.Booked), listed.Status);

        using var cancelResponse = await client.PutAsync(
            $"/api/me/appointments/{booked.Id}/cancel",
            content: null);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        var afterCancellation = await ReadPageAsync(client);
        var cancelled = Assert.Single(afterCancellation.Items, item => item.Id == booked.Id);
        Assert.Equal(nameof(AppointmentStatus.Cancelled), cancelled.Status);
    }

    private static async Task<PagedResult<PatientAppointmentDto>> ReadPageAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/me/appointments?pageSize=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedResult<PatientAppointmentDto>>();
        Assert.NotNull(page);

        return page;
    }

    private async Task SeedAppointmentsAsync(
        Guid patientId,
        int doctorId,
        int count,
        DateTime firstStartUtc)
    {
        await using var db = _factory.CreateDbContext();

        var slots = Enumerable.Range(0, count)
            .Select(index =>
            {
                var start = firstStartUtc.AddMinutes(index * 30);

                return new DoctorSlot
                {
                    DoctorId = doctorId,
                    StartTimeUtc = start,
                    EndTimeUtc = start.AddMinutes(30),
                    IsAvailable = false
                };
            })
            .ToList();

        db.DoctorSlots.AddRange(slots);
        await db.SaveChangesAsync();

        db.Appointments.AddRange(slots.Select(slot => new Appointment
        {
            PatientId = patientId,
            DoctorId = doctorId,
            DoctorSlotId = slot.Id,
            Status = AppointmentStatus.Booked,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        }));

        await db.SaveChangesAsync();
    }
}

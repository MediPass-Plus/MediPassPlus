using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using MediPassPlus.Application.AppointmentStatusFlow;
using MediPassPlus.Application.DoctorSchedule;
using MediPassPlus.Application.VisitAccess;
using MediPassPlus.Domain.Audit;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Domain.VisitAccess;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// HTTP matrix for the appointment status flow and the doctor daily schedule: authentication, authorization, the
/// clinic and ownership scope, the state machine, the UTC timestamps, the Cairo calendar and the end-to-end
/// check-in -> start-visit -> complete path (FR-BKG-08, FR-STF-03, FR-ACS-08, FR-BKG-07, NFR-SEC-10).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class AppointmentStatusEndpointTests
{
    private readonly MediPassApiFactory _factory;

    /// <summary>Initializes the test class with the shared API factory.</summary>
    /// <param name="factory">The collection fixture that hosts the API and database.</param>
    public AppointmentStatusEndpointTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CheckIn_WithoutToken_ReturnsUnauthorized()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var client = _factory.CreateClient();

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/check-in",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(DefaultRoles.Doctor)]
    [InlineData(DefaultRoles.Patient)]
    [InlineData(DefaultRoles.Admin)]
    public async Task CheckIn_WithNonStaffRole_ReturnsForbidden(string role)
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/check-in",
            content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CheckIn_WhenTheAppointmentIsInAnotherClinic_ReturnsNotFound()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.OtherClinicStaffUser);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/check-in",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CheckIn_WhenTheAppointmentIsNotToday_ReturnsBadRequest()
    {
        var tomorrow = _factory.ClinicDateAtNineUtc(_factory.ClinicToday.AddDays(1));
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync(slotStartUtc: tomorrow);
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/check-in",
            content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
    }

    [Fact]
    public async Task CheckIn_WhenTheAppointmentIsNotBooked_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync(AppointmentStatus.CheckedIn);
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/check-in",
            content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CheckIn_WhenTheAppointmentIsBookedToday_ChecksItInAndStoresTheTimestamp()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/check-in",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = await response.Content.ReadFromJsonAsync<StaffAppointmentDto>();
        Assert.NotNull(view);
        Assert.Equal(scenario.Appointment.Id, view.AppointmentId);
        Assert.Equal("Patient Integration Test", view.PatientName);
        Assert.Equal(nameof(AppointmentStatus.CheckedIn), view.Status);
        Assert.InRange(view.CheckedInAtUtc, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        await using var db = _factory.CreateDbContext();
        var stored = await db.Appointments.AsNoTracking()
            .SingleAsync(appointment => appointment.Id == scenario.Appointment.Id);
        Assert.Equal(AppointmentStatus.CheckedIn, stored.Status);
        Assert.NotNull(stored.CheckedInAtUtc);
        Assert.Null(stored.InVisitAtUtc);
        Assert.Null(stored.CompletedAtUtc);
    }

    [Fact]
    public async Task CheckIn_WhenCalledTwice_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var first = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/check-in",
            content: null);
        using var second = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/check-in",
            content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Complete_WithoutToken_ReturnsUnauthorized()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync(AppointmentStatus.InVisit);
        using var client = _factory.CreateClient();

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/complete",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Patient)]
    [InlineData(DefaultRoles.Admin)]
    public async Task Complete_WithNonDoctorRole_ReturnsForbidden(string role)
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync(AppointmentStatus.InVisit);
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/complete",
            content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Complete_WhenAnotherDoctorOwnsTheAppointment_ReturnsForbidden()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync(AppointmentStatus.InVisit);
        using var client = _factory.CreateAuthenticatedClient(scenario.OtherClinicDoctorUser);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/complete",
            content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Complete_WhenTheAppointmentIsNotInVisit_ReturnsBadRequest()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync(AppointmentStatus.CheckedIn);
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/complete",
            content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Complete_WhenTheVisitIsInProgress_CompletesItAndStoresTheTimestamp()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync(AppointmentStatus.InVisit);
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/complete",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<DoctorAppointmentStatusDto>();
        Assert.NotNull(status);
        Assert.Equal(scenario.Appointment.Id, status.AppointmentId);
        Assert.Equal(nameof(AppointmentStatus.Completed), status.Status);
        Assert.InRange(status.CompletedAtUtc, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        await using var db = _factory.CreateDbContext();
        var stored = await db.Appointments.AsNoTracking()
            .SingleAsync(appointment => appointment.Id == scenario.Appointment.Id);
        Assert.Equal(AppointmentStatus.Completed, stored.Status);
        Assert.NotNull(stored.CompletedAtUtc);
        Assert.False(await db.AuditLogs.AsNoTracking()
            .AnyAsync(log => log.PatientId == scenario.PatientUser.Id && log.Action == AuditAction.AccessEnded));
    }

    [Fact]
    public async Task Complete_WhenTheVisitHasAnActiveGrant_SchedulesTheEndAndAudits()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync(AppointmentStatus.InVisit);
        var grant = await _factory.CreateGrantAsync(scenario.Appointment.Id);
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);
        var before = DateTime.UtcNow;

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/complete",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        var stored = await db.AccessGrants.AsNoTracking().SingleAsync(candidate => candidate.Id == grant.Id);
        Assert.NotNull(stored.EndedAtUtc);
        Assert.InRange(
            stored.EndedAtUtc.Value,
            before.AddMinutes(14),
            DateTime.UtcNow.AddMinutes(16));
        Assert.True(stored.EndedAtUtc < stored.ExpiresAtUtc);
        Assert.Equal(GrantEndReason.VisitCompleted, stored.EndReason);

        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(log => log.AccessGrantId == grant.Id);
        Assert.Equal(AuditAction.AccessEnded, audit.Action);
        Assert.Equal(AuditEntityType.AccessGrant, audit.EntityType);
        Assert.Equal(scenario.DoctorUser.Id, audit.UserId);
        Assert.Equal(scenario.PatientUser.Id, audit.PatientId);
        Assert.Equal(grant.Id, audit.AccessGrantId);
    }

    [Fact]
    public async Task Complete_WhenTheGraceWouldPassTheTwoHourLimit_CapsTheEndAtExpiry()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync(AppointmentStatus.InVisit);
        var grant = await _factory.CreateGrantAsync(
            scenario.Appointment.Id,
            grantedAtUtc: DateTime.UtcNow.AddMinutes(-110));
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await client.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/complete",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        var stored = await db.AccessGrants.AsNoTracking().SingleAsync(candidate => candidate.Id == grant.Id);
        Assert.NotNull(stored.EndedAtUtc);
        Assert.Equal(stored.ExpiresAtUtc, stored.EndedAtUtc);
        Assert.Equal(GrantEndReason.VisitCompleted, stored.EndReason);
    }

    [Fact]
    public async Task Complete_WhenTheGrantAlreadyEnded_DoesNotExtendItOrWriteASecondAudit()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync(AppointmentStatus.InVisit);
        var grant = await _factory.CreateGrantAsync(scenario.Appointment.Id);
        using var doctorClient = _factory.CreateAuthenticatedClient(scenario.DoctorUser);
        using var patientClient = _factory.CreateAuthenticatedClient(scenario.PatientUser);

        using var end = await patientClient.PostAsync(
            $"/api/access-grants/{grant.Id}/end",
            content: null);
        Assert.Equal(HttpStatusCode.OK, end.StatusCode);
        var ended = await end.Content.ReadFromJsonAsync<AccessGrantEndedResponse>();
        Assert.NotNull(ended);

        using var response = await doctorClient.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/complete",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = _factory.CreateDbContext();
        var stored = await db.AccessGrants.AsNoTracking().SingleAsync(candidate => candidate.Id == grant.Id);
        Assert.Equal(ended.EndedAtUtc, stored.EndedAtUtc);
        Assert.Equal(GrantEndReason.PatientEnded, stored.EndReason);
        Assert.Equal(
            1,
            await db.AuditLogs.AsNoTracking().CountAsync(log => log.AccessGrantId == grant.Id));
        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(log => log.AccessGrantId == grant.Id);
        Assert.Equal(scenario.PatientUser.Id, audit.UserId);
    }

    [Fact]
    public async Task CheckIn_StartVisit_Complete_RunsTheWholeStatusFlow()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var staffClient = _factory.CreateAuthenticatedClient(scenario.StaffUser);
        using var doctorClient = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var checkIn = await staffClient.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/check-in",
            content: null);
        Assert.Equal(HttpStatusCode.OK, checkIn.StatusCode);

        using var start = await doctorClient.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/start-visit",
            content: null);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var started = await start.Content.ReadFromJsonAsync<StartVisitResponse>();
        Assert.NotNull(started);
        Assert.False(string.IsNullOrWhiteSpace(started.QrToken));

        await using (var afterStart = _factory.CreateDbContext())
        {
            var stored = await afterStart.Appointments.AsNoTracking()
                .SingleAsync(appointment => appointment.Id == scenario.Appointment.Id);
            Assert.Equal(AppointmentStatus.InVisit, stored.Status);
            Assert.NotNull(stored.CheckedInAtUtc);
            Assert.NotNull(stored.InVisitAtUtc);
            Assert.Null(stored.CompletedAtUtc);
        }

        using var complete = await doctorClient.PostAsync(
            $"/api/appointments/{scenario.Appointment.Id}/complete",
            content: null);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        await using var afterComplete = _factory.CreateDbContext();
        var completed = await afterComplete.Appointments.AsNoTracking()
            .SingleAsync(appointment => appointment.Id == scenario.Appointment.Id);
        Assert.Equal(AppointmentStatus.Completed, completed.Status);
        Assert.NotNull(completed.CheckedInAtUtc);
        Assert.NotNull(completed.InVisitAtUtc);
        Assert.NotNull(completed.CompletedAtUtc);
    }

    [Fact]
    public async Task GetSchedule_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/doctors/me/schedule?date={FormatDate(_factory.ClinicToday)}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(DefaultRoles.Staff)]
    [InlineData(DefaultRoles.Patient)]
    [InlineData(DefaultRoles.Admin)]
    public async Task GetSchedule_WithNonDoctorRole_ReturnsForbidden(string role)
    {
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await client.GetAsync(
            $"/api/doctors/me/schedule?date={FormatDate(_factory.ClinicToday)}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetSchedule_WhenTheUserHasNoDoctorProfile_ReturnsForbidden()
    {
        var user = await _factory.CreateUserAsync(DefaultRoles.Doctor);
        using var client = _factory.CreateAuthenticatedClient(user);

        using var response = await client.GetAsync(
            $"/api/doctors/me/schedule?date={FormatDate(_factory.ClinicToday)}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetSchedule_ReturnsOnlyTheDoctorsTodayAppointmentsOrderedByStart()
    {
        var scenario = await _factory.CreateDoctorScheduleScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.DoctorUser);

        using var response = await client.GetAsync(
            $"/api/doctors/me/schedule?date={FormatDate(_factory.ClinicToday)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<DoctorScheduleItemDto>>();
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);

        Assert.Equal(scenario.BookedAppointment.Id, items[0].AppointmentId);
        Assert.Equal("Schedule Patient A", items[0].PatientName);
        Assert.Equal(nameof(AppointmentStatus.Booked), items[0].Status);
        Assert.Equal(scenario.CheckedInAppointment.Id, items[1].AppointmentId);
        Assert.Equal("Schedule Patient B", items[1].PatientName);
        Assert.Equal(nameof(AppointmentStatus.CheckedIn), items[1].Status);
        Assert.True(items[0].StartTimeUtc < items[1].StartTimeUtc);

        Assert.DoesNotContain(items, item => item.AppointmentId == scenario.CancelledAppointment.Id);
        Assert.DoesNotContain(items, item => item.AppointmentId == scenario.OtherDoctorAppointment.Id);
        Assert.DoesNotContain(items, item => item.AppointmentId == scenario.TomorrowAppointment.Id);
    }

    [Fact]
    public async Task GetSchedule_UsesTheCairoDayBoundary()
    {
        var startOfDayUtc = _factory.ClinicDateAtNineUtc(_factory.ClinicToday).AddHours(-9);

        // A slot just after Cairo midnight still belongs to today, even though its UTC date is yesterday.
        var early = await _factory.CreateAppointmentStatusScenarioAsync(
            slotStartUtc: startOfDayUtc.AddMinutes(30));
        using var earlyClient = _factory.CreateAuthenticatedClient(early.DoctorUser);

        using var earlyResponse = await earlyClient.GetAsync(
            $"/api/doctors/me/schedule?date={FormatDate(_factory.ClinicToday)}");

        Assert.Equal(HttpStatusCode.OK, earlyResponse.StatusCode);
        var earlyItems = await earlyResponse.Content.ReadFromJsonAsync<List<DoctorScheduleItemDto>>();
        Assert.NotNull(earlyItems);
        Assert.Contains(earlyItems, item => item.AppointmentId == early.Appointment.Id);

        // A slot just before the next Cairo midnight still belongs to today, even if its UTC date is tomorrow.
        var nextStartOfDayUtc = _factory.ClinicDateAtNineUtc(_factory.ClinicToday.AddDays(1)).AddHours(-9);
        var late = await _factory.CreateAppointmentStatusScenarioAsync(
            slotStartUtc: nextStartOfDayUtc.AddMinutes(-30));
        using var lateClient = _factory.CreateAuthenticatedClient(late.DoctorUser);

        using var lateResponse = await lateClient.GetAsync(
            $"/api/doctors/me/schedule?date={FormatDate(_factory.ClinicToday)}");

        Assert.Equal(HttpStatusCode.OK, lateResponse.StatusCode);
        var lateItems = await lateResponse.Content.ReadFromJsonAsync<List<DoctorScheduleItemDto>>();
        Assert.NotNull(lateItems);
        Assert.Contains(lateItems, item => item.AppointmentId == late.Appointment.Id);
    }

    private static string FormatDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

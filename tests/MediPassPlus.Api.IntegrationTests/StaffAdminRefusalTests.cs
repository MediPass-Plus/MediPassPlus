using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediPassPlus.Api.Controllers;
using MediPassPlus.Application.Staff;
using MediPassPlus.Domain.Clinical;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// MP-41: Staff and Admin are refused (403) on every medical and visit-access endpoint, never reach another
/// clinic's data, and receive no medical content (FR-PRV-06, FR-STF-04/05/09, NFR-SEC-10, NFR-PRI-04, ADR-0014).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class StaffAdminRefusalTests : IClassFixture<MediPassApiFactory>
{
    private static readonly Guid AnyGuid = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // Controllers that are not part of the medical or visit-access surface. Every other controller in the API
    // assembly is treated as medical, so a new medical controller is discovered without editing this list.
    private static readonly HashSet<Type> NonMedicalControllers =
    [
        typeof(AdminAccountsController),
        typeof(AdminClinicsController),
        typeof(AdminUsersController),
        typeof(AuthController),
        typeof(ClinicDoctorsController),
        typeof(ClinicsController),
        typeof(DoctorsController),
        typeof(DoctorSlotsController),
        typeof(MeProfileController),
        typeof(StaffAppointmentsController),
        typeof(StaffScheduleController),
        typeof(StaffSlotsController),
        typeof(WeatherForecastController)
    ];

    // The only staff action that lives on a controller that also has medical actions. Every other staff action is
    // on a controller in NonMedicalControllers. A new staff action must be added here explicitly, so the discovery
    // test cannot silently skip a medical action that was given the Staff role.
    private static readonly HashSet<string> StaffWorkflowActions = new(StringComparer.Ordinal)
    {
        $"{typeof(AppointmentStatusController).FullName}.{nameof(AppointmentStatusController.CheckIn)}"
    };

    // Explicit matrix: every medical or visit-access endpoint of the product API.
    public static IEnumerable<object[]> MedicalEndpoints()
    {
        (string Method, string Path, string? Body)[] endpoints =
        [
            ("POST", "/api/appointments/1/diagnoses", """{"description":"x"}"""),
            ("POST", $"/api/diagnoses/{AnyGuid}/prescriptions", """{"medicineName":"x","dosage":"1"}"""),
            ("GET", "/api/appointments/1/history", null),
            ("GET", "/api/me/history", null),
            ("GET", "/api/me/appointments", null),
            ("POST", "/api/me/appointments", """{"doctorId":1,"slotId":1}"""),
            ("PUT", "/api/me/appointments/1/cancel", null),
            ("PUT", "/api/me/appointments/1/reschedule", """{"slotId":1}"""),
            ("POST", "/api/appointments/1/start-visit", null),
            ("POST", "/api/appointments/1/access-code", null),
            ("POST", "/api/appointments/1/verify-code", """{"code":"123456"}"""),
            ("GET", "/api/appointments/1/access-state", null),
            ("POST", "/api/appointments/1/complete", null),
            ("POST", "/api/access/scan", """{"token":"x"}"""),
            ("POST", "/api/access-requests/scan-image", null),
            ("POST", "/api/access-requests/token/approve", """{"token":"x"}"""),
            ("POST", "/api/access-requests/token/reject", """{"token":"x"}"""),
            ("POST", $"/api/access-requests/{AnyGuid}/approve", null),
            ("POST", $"/api/access-requests/{AnyGuid}/reject", null),
            ("POST", $"/api/access-grants/{AnyGuid}/end", null),
            ("GET", "/api/doctors/me/schedule?date=2030-01-01", null)
        ];

        foreach (var role in new[] { DefaultRoles.Staff, DefaultRoles.Admin })
        {
            foreach (var e in endpoints)
            {
                yield return [role, e.Method, e.Path, e.Body!];
            }
        }
    }

    private readonly MediPassApiFactory _factory;

    public StaffAdminRefusalTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [MemberData(nameof(MedicalEndpoints))]
    public async Task StaffAndAdmin_AreRefusedOnEveryMedicalAndVisitAccessEndpoint(
        string role, string method, string path, string? body)
    {
        var user = await _factory.CreateUserAsync(role);
        using var client = _factory.CreateAuthenticatedClient(user);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Discovery: every controller in the API assembly that is not explicitly non-medical is treated as medical.
    // Each such action must have an explicit Doctor/Patient-only role restriction and must refuse Staff and Admin,
    // so a medical endpoint added later is covered without editing this file.
    [Fact]
    public async Task EveryDiscoveredMedicalEndpoint_RefusesStaffAndAdmin()
    {
        var provider = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var staff = await _factory.CreateUserAsync(DefaultRoles.Staff);
        var admin = await _factory.CreateUserAsync(DefaultRoles.Admin);
        using var staffClient = _factory.CreateAuthenticatedClient(staff);
        using var adminClient = _factory.CreateAuthenticatedClient(admin);
        var checkedCount = 0;
        var staffActionSeen = false;

        foreach (var action in provider.ActionDescriptors.Items.OfType<ControllerActionDescriptor>())
        {
            // Test-only controllers (for example ThrowingTestController) are not part of the product surface.
            if (action.ControllerTypeInfo.Assembly != typeof(Program).Assembly)
            {
                continue;
            }

            if (NonMedicalControllers.Contains(action.ControllerTypeInfo.AsType()))
            {
                continue;
            }

            var actionId = $"{action.ControllerTypeInfo.FullName}.{action.ActionName}";
            var roles = GetEffectiveRoles(action);

            if (StaffWorkflowActions.Contains(actionId))
            {
                staffActionSeen = true;
                Assert.True(
                    roles.SetEquals([DefaultRoles.Staff]),
                    $"{actionId} is a staff workflow action but its roles are '{string.Join(",", roles)}'.");
                continue;
            }

            Assert.False(
                roles.Count == 0,
                $"{actionId} is on the medical surface but has no explicit role restriction. Add " +
                "[Authorize(Roles = \"Doctor\")] or [Authorize(Roles = \"Patient\")], or add its controller to " +
                "NonMedicalControllers after review.");

            Assert.DoesNotContain(DefaultRoles.Staff, roles);
            Assert.DoesNotContain(DefaultRoles.Admin, roles);
            Assert.All(
                roles,
                role => Assert.True(
                    role is DefaultRoles.Doctor or DefaultRoles.Patient,
                    $"{actionId} allows the unexpected role '{role}'."));

            var template = action.AttributeRouteInfo?.Template;
            Assert.False(
                string.IsNullOrWhiteSpace(template),
                $"{actionId} has no attribute route template, so the refusal probe cannot address it.");

            var httpMethods = action.ActionConstraints?
                .OfType<HttpMethodActionConstraint>()
                .SelectMany(constraint => constraint.HttpMethods)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
            Assert.NotEmpty(httpMethods);

            var path = MaterializeRoute(template!);
            foreach (var httpMethod in httpMethods)
            {
                foreach (var (client, role) in new[]
                {
                    (staffClient, DefaultRoles.Staff),
                    (adminClient, DefaultRoles.Admin)
                })
                {
                    using var request = new HttpRequestMessage(new HttpMethod(httpMethod), path);
                    using var response = await client.SendAsync(request);

                    Assert.True(
                        response.StatusCode == HttpStatusCode.Forbidden,
                        $"{role} {httpMethod} {path} returned {(int)response.StatusCode} " +
                        $"({response.StatusCode}) instead of 403.");
                    checkedCount++;
                }
            }
        }

        Assert.True(checkedCount > 0, "The discovery test found no medical endpoints.");
        Assert.True(staffActionSeen, "The staff-workflow allowlist is stale: no listed action was found.");
    }

    // Cross-clinic: another clinic's staff cannot read the schedule or mutate slot, booking or check-in data.
    [Fact]
    public async Task OtherClinicStaff_CannotReadOrMutateTheClinicsData()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var other = _factory.CreateAuthenticatedClient(scenario.OtherClinicStaffUser);
        using var own = _factory.CreateAuthenticatedClient(scenario.StaffUser);
        var d = scenario.Doctor.Id;
        var schedulePath = $"/api/staff/schedule?date={_factory.ClinicToday:yyyy-MM-dd}";
        var start = _factory.ClinicDateAtNineUtc(_factory.ClinicToday.AddDays(30));
        var slotBody = new { startTimeUtc = start, endTimeUtc = start.AddMinutes(30) };

        using var addSlot = await other.PostAsJsonAsync($"/api/staff/doctors/{d}/slots", slotBody);
        using var delSlot = await other.DeleteAsync($"/api/staff/doctors/{d}/slots/{scenario.Slot.Id}");
        using var book = await other.PostAsJsonAsync(
            "/api/staff/appointments",
            new { patientEmail = scenario.PatientUser.Email, doctorId = d, slotId = scenario.Slot.Id });
        using var checkIn = await other.PostAsync(
            $"/api/staff/appointments/{scenario.Appointment.Id}/check-in", null);
        using var otherSchedule = await other.GetAsync(schedulePath);
        using var ownSchedule = await own.GetAsync(schedulePath);

        Assert.Equal(HttpStatusCode.NotFound, addSlot.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delSlot.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, book.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, checkIn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherSchedule.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownSchedule.StatusCode);

        var otherItems = await otherSchedule.Content.ReadFromJsonAsync<List<StaffScheduleItemDto>>();
        var ownItems = await ownSchedule.Content.ReadFromJsonAsync<List<StaffScheduleItemDto>>();
        Assert.NotNull(otherItems);
        Assert.NotNull(ownItems);
        Assert.DoesNotContain(otherItems, item => item.AppointmentId == scenario.Appointment.Id);
        Assert.Contains(ownItems, item => item.AppointmentId == scenario.Appointment.Id);

        // Admin has no clinic scope at all: the whole staff surface is Staff-only, not cross-clinic Admin.
        var admin = await _factory.CreateUserAsync(DefaultRoles.Admin);
        using var adminClient = _factory.CreateAuthenticatedClient(admin);
        using var adminSchedule = await adminClient.GetAsync(schedulePath);
        using var adminAddSlot = await adminClient.PostAsJsonAsync($"/api/staff/doctors/{d}/slots", slotBody);
        Assert.Equal(HttpStatusCode.Forbidden, adminSchedule.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, adminAddSlot.StatusCode);

        // A refusal must leave the other clinic's data untouched, not merely return 404.
        await using var db = _factory.CreateDbContext();
        Assert.True(await db.DoctorSlots.AnyAsync(slot => slot.Id == scenario.Slot.Id));
        Assert.False(await db.DoctorSlots.AnyAsync(
            slot => slot.DoctorId == d && slot.StartTimeUtc == start));
        Assert.Equal(
            AppointmentStatus.Booked,
            (await db.Appointments.AsNoTracking()
                .SingleAsync(appointment => appointment.Id == scenario.Appointment.Id)).Status);
        Assert.Equal(1, await db.Appointments.CountAsync(appointment => appointment.DoctorId == d));
    }

    // Response shape: the schedule returns exactly the five ADR-0014 fields and no medical content.
    [Fact]
    public async Task StaffSchedule_ContainsOnlyTheApprovedFields_AndNoMedicalText()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        const string patientPhone = "+20 100 123 4567";
        string patientName;

        await using (var db = _factory.CreateDbContext())
        {
            var patient = await db.Users.SingleAsync(user => user.Id == scenario.PatientUser.Id);
            patient.PhoneNumber = patientPhone;
            await db.SaveChangesAsync();
            patientName = patient.FullName;
        }

        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);
        using var response = await client.GetAsync(
            $"/api/staff/schedule?date={_factory.ClinicToday:yyyy-MM-dd}");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.EnumerateArray().ToArray();
        Assert.NotEmpty(items);

        // The exact property set is the proof that no extra field, medical or otherwise, is returned.
        var expectedFields = new[]
        {
            "appointmentId", "patientName", "patientPhone", "startTimeUtc", "status"
        };
        foreach (var item in items)
        {
            Assert.Equal(
                expectedFields.Order(StringComparer.Ordinal).ToArray(),
                item.EnumerateObject().Select(property => property.Name)
                    .Order(StringComparer.Ordinal).ToArray());
        }

        var appointment = Assert.Single(
            items,
            item => item.GetProperty("appointmentId").GetInt32() == scenario.Appointment.Id);
        Assert.Equal(scenario.Appointment.Id, appointment.GetProperty("appointmentId").GetInt32());
        Assert.Equal(patientName, appointment.GetProperty("patientName").GetString());
        Assert.Equal(patientPhone, appointment.GetProperty("patientPhone").GetString());
        Assert.Equal(scenario.Slot.StartTimeUtc, appointment.GetProperty("startTimeUtc").GetDateTime());
        Assert.Equal(scenario.Appointment.Status.ToString(), appointment.GetProperty("status").GetString());

        Assert.DoesNotContain(scenario.PatientUser.Email, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diagnos", json, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> GetEffectiveRoles(ControllerActionDescriptor action)
    {
        var attributes = action.MethodInfo.GetCustomAttributes(true)
            .Concat(action.ControllerTypeInfo.GetCustomAttributes(true))
            .ToArray();

        Assert.False(
            attributes.OfType<IAllowAnonymous>().Any(),
            $"{action.ControllerTypeInfo.FullName}.{action.ActionName} is [AllowAnonymous] on the medical surface.");

        return attributes
            .OfType<AuthorizeAttribute>()
            .Select(attribute => attribute.Roles)
            .Where(roles => !string.IsNullOrWhiteSpace(roles))
            .SelectMany(roles => roles!.Split(',', StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string MaterializeRoute(string template)
    {
        var path = Regex.Replace(template, @"\{[^}:]+:guid\??\}", AnyGuid.ToString());
        path = Regex.Replace(path, @"\{[^}:]+:int\??\}", "1");
        path = Regex.Replace(path, @"\{[^}:]+\??\}", "1");

        Assert.DoesNotContain("{", path);
        Assert.DoesNotContain("}", path);
        return "/" + path.TrimStart('~', '/');
    }
}

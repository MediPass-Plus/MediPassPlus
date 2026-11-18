using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MediPassPlus.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Abstractions;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// MP-41: Staff and Admin are refused (403) on every medical and visit-access endpoint, never reach another
/// clinic's data, and receive no medical content (FR-PRV-06, FR-STF-04/05/09, NFR-SEC-10, NFR-PRI-04).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class StaffAdminRefusalTests : IClassFixture<MediPassApiFactory>
{
    private static readonly Guid AnyGuid = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // Explicit matrix: every medical or visit-access endpoint of the product API.
    public static IEnumerable<object[]> MedicalEndpoints()
    {
        (string Method, string Path, string? Body)[] endpoints =
        [
            ("POST", "/api/appointments/1/diagnoses", """{"description":"x"}"""),
            ("POST", $"/api/diagnoses/{AnyGuid}/prescriptions", """{"medicineName":"x","dosage":"1"}"""),
            ("GET", "/api/appointments/1/history", null),
            ("GET", "/api/me/history", null),
            ("POST", "/api/appointments/1/start-visit", null),
            ("POST", "/api/appointments/1/access-code", null),
            ("POST", "/api/appointments/1/verify-code", """{"code":"123456"}"""),
            ("GET", "/api/appointments/1/access-state", null),
            ("POST", "/api/appointments/1/complete", null),
            ("POST", "/api/access/scan", """{"token":"x"}"""),
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

    // Discovery: any endpoint that is restricted to Doctor/Patient roles must also refuse Staff and Admin,
    // so a medical endpoint added later is covered without editing this file.
    [Fact]
    public async Task EveryDoctorOrPatientOnlyEndpoint_RefusesStaffAndAdmin()
    {
        var provider = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var staff = await _factory.CreateUserAsync(DefaultRoles.Staff);
        var admin = await _factory.CreateUserAsync(DefaultRoles.Admin);
        using var staffClient = _factory.CreateAuthenticatedClient(staff);
        using var adminClient = _factory.CreateAuthenticatedClient(admin);
        var checkedCount = 0;

        foreach (var action in provider.ActionDescriptors.Items.OfType<ControllerActionDescriptor>())
        {
            if (action.ControllerTypeInfo.Namespace != "MediPassPlus.Api.Controllers")
            {
                continue;
            }

            var roles = action.MethodInfo.GetCustomAttributes(true)
                .Concat(action.ControllerTypeInfo.GetCustomAttributes(true))
                .OfType<AuthorizeAttribute>()
                .Select(a => a.Roles)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .SelectMany(r => r!.Split(',', StringSplitOptions.TrimEntries))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (roles.Count == 0
                || roles.Contains(DefaultRoles.Staff)
                || roles.Contains(DefaultRoles.Admin)
                || roles.Any(r => r is not (DefaultRoles.Doctor or DefaultRoles.Patient)))
            {
                continue;
            }

            var template = action.AttributeRouteInfo?.Template;
            var httpMethod = action.ActionConstraints?
                .OfType<Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint>()
                .FirstOrDefault()?.HttpMethods.FirstOrDefault();
            if (template is null || httpMethod is null)
            {
                continue;
            }

            var path = "/" + System.Text.RegularExpressions.Regex.Replace(
                template, @"\{(\w+)(:guid)\??[^}]*\}", AnyGuid.ToString());
            path = System.Text.RegularExpressions.Regex.Replace(path, @"\{\w+(:int)?\??[^}]*\}", "1");
            if (path.Contains('{'))
            {
                continue;
            }

            foreach (var client in new[] { staffClient, adminClient })
            {
                using var request = new HttpRequestMessage(new HttpMethod(httpMethod), path);
                using var response = await client.SendAsync(request);
                Assert.True(
                    response.StatusCode == HttpStatusCode.Forbidden,
                    $"{httpMethod} {path} returned {(int)response.StatusCode} instead of 403.");
                checkedCount++;
            }
        }

        Assert.True(checkedCount > 0, "The discovery test found no Doctor/Patient-only endpoints.");
    }

    // Cross-clinic: another clinic's staff cannot reach slot, booking or check-in data.
    [Fact]
    public async Task OtherClinicStaff_CannotReadOrMutateTheClinicsData()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var other = _factory.CreateAuthenticatedClient(scenario.OtherClinicStaffUser);
        var d = scenario.Doctor.Id;

        using var addSlot = await other.PostAsJsonAsync(
            $"/api/staff/doctors/{d}/slots",
            new { startTimeUtc = "2031-01-01T09:00:00Z", endTimeUtc = "2031-01-01T09:30:00Z" });
        using var delSlot = await other.DeleteAsync($"/api/staff/doctors/{d}/slots/{scenario.Slot.Id}");
        using var book = await other.PostAsJsonAsync(
            "/api/staff/appointments",
            new { patientEmail = scenario.PatientUser.Email, doctorId = d, slotId = scenario.Slot.Id });
        using var checkIn = await other.PostAsync(
            $"/api/staff/appointments/{scenario.Appointment.Id}/check-in", null);

        Assert.Equal(HttpStatusCode.NotFound, addSlot.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delSlot.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, book.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, checkIn.StatusCode);
    }

    // Response shape: the staff schedule leaks no medical content.
    [Fact]
    public async Task StaffSchedule_ContainsOnlyTheApprovedFields_AndNoMedicalText()
    {
        var scenario = await _factory.CreateAppointmentStatusScenarioAsync();
        using var client = _factory.CreateAuthenticatedClient(scenario.StaffUser);

        using var response = await client.GetAsync(
            $"/api/staff/schedule?date={_factory.ClinicToday:yyyy-MM-dd}");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(json);
        Assert.NotEmpty(doc.RootElement.EnumerateArray());

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "appointmentId", "patientName", "patientPhone", "startTimeUtc", "status"
        };
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            foreach (var property in item.EnumerateObject())
            {
                Assert.Contains(property.Name, allowed);
            }
        }

        foreach (var word in new[] { "diagnos", "prescription", "report", "medical", "history" })
        {
            Assert.DoesNotContain(word, json, StringComparison.OrdinalIgnoreCase);
        }
    }
}
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// MP-55 / FR-ACS-10: the route inventory exposes no patient list or patient search to a Doctor. Every route a
/// Doctor can call is scoped by an appointment or a diagnosis id (verified by the grant filter) or is the
/// doctor's own schedule. A new route that breaks this rule fails here.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class DoctorRouteInventoryTests : IClassFixture<MediPassApiFactory>
{
    private static readonly string[] AllowedUnscopedRoutes = ["api/doctors/me/schedule"];

    private readonly MediPassApiFactory _factory;

    public DoctorRouteInventoryTests(MediPassApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void NoDoctorRoute_ExposesAPatientListOrSearch()
    {
        var doctorActions = GetDoctorActions().ToList();

        Assert.NotEmpty(doctorActions);

        foreach (var action in doctorActions)
        {
            var template = action.AttributeRouteInfo?.Template ?? string.Empty;

            Assert.False(
                template.Contains("patient", StringComparison.OrdinalIgnoreCase)
                || template.Contains("search", StringComparison.OrdinalIgnoreCase),
                $"Doctor route exposes a patient or search path: {template}");

            foreach (var parameter in action.Parameters)
            {
                Assert.False(
                    parameter.Name.Contains("patient", StringComparison.OrdinalIgnoreCase)
                    || parameter.Name.Contains("search", StringComparison.OrdinalIgnoreCase),
                    $"Doctor route takes a patient or search parameter '{parameter.Name}': {template}");
            }

            var scoped = template.Contains("{appointmentId", StringComparison.OrdinalIgnoreCase)
                || template.Contains("{diagnosisId", StringComparison.OrdinalIgnoreCase)
                || AllowedUnscopedRoutes.Contains(template, StringComparer.OrdinalIgnoreCase);

            Assert.True(scoped, $"Doctor route is not scoped to an appointment or diagnosis: {template}");
        }
    }

    private IEnumerable<ControllerActionDescriptor> GetDoctorActions()
    {
        var provider = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>();

        foreach (var action in provider.ActionDescriptors.Items.OfType<ControllerActionDescriptor>())
        {
            var roles = action.MethodInfo.GetCustomAttributes(true)
                .Concat(action.ControllerTypeInfo.GetCustomAttributes(true))
                .OfType<AuthorizeAttribute>()
                .Select(a => a.Roles)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .SelectMany(r => r!.Split(',', StringSplitOptions.TrimEntries));

            if (roles.Contains("Doctor", StringComparer.OrdinalIgnoreCase))
            {
                yield return action;
            }
        }
    }
}
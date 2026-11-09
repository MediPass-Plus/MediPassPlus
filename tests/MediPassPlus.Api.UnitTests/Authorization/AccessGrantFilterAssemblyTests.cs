using System.Reflection;
using MediPassPlus.Api.Authorization;
using MediPassPlus.Api.Controllers;
using MediPassPlus.Application.Privacy;
using Microsoft.AspNetCore.Mvc;

namespace MediPassPlus.Api.UnitTests.Authorization;

public class AccessGrantFilterAssemblyTests
{
    private static readonly Assembly ApiAssembly = typeof(VisitAccessController).Assembly;

    [Fact]
    public void TheApiAssembly_ContainsNoAccessGrantCheckerImplementation()
    {
        // The gate is an interface in Application, implemented once in Infrastructure. A bypass in the API assembly
        // would let a medical endpoint skip the database check (FR-PRV-01, NFR-PRI-01).
        var implementations = ApiAssembly
            .GetTypes()
            .Where(type => typeof(IAccessGrantChecker).IsAssignableFrom(type))
            .ToArray();

        Assert.Empty(implementations);
    }

    [Fact]
    public void TheTestFake_IsNotPartOfTheApiAssembly()
    {
        var fake = new FakeAccessGrantChecker();

        Assert.NotEqual(ApiAssembly, fake.GetType().Assembly);
        Assert.Contains("Fake", fake.GetType().Name, StringComparison.Ordinal);
    }

    [Fact]
    public void TheApiAssembly_ContainsNoTypeThatLooksLikeATestFake()
    {
        var suspects = ApiAssembly
            .GetTypes()
            .Where(type => type.Name.Contains("Fake", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(suspects);
    }

    [Fact]
    public void TheGrantFilter_IsRegisteredOnlyThroughTheAttribute()
    {
        // AccessGrantFilter depends on the scoped checker, so the attribute resolves it per request.
        Assert.True(typeof(RequireActiveGrantAttribute).IsSubclassOf(typeof(TypeFilterAttribute)));
        Assert.Equal(typeof(AccessGrantFilter), new RequireActiveGrantAttribute().ImplementationType);
    }

    private sealed class FakeAccessGrantChecker : IAccessGrantChecker
    {
        public Task<AccessGrantCheckResult> RequireActiveGrantAsync(
            Guid doctorUserId,
            int appointmentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AccessGrantCheckResult.Denied(AccessGrantCheckFailure.GrantNotFound));
    }
}

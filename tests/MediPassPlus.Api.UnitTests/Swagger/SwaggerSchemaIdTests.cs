using MediPassPlus.Api.Swagger;
using MediPassPlus.Application.Clinical;
using MediPassPlus.Application.Common;
using AdminClinicDto = MediPassPlus.Application.Dtos.AdminManagement.ClinicDto;

namespace MediPassPlus.Api.UnitTests.Swagger;

/// <summary>
/// Guards the namespace-qualified schema ids used by Swashbuckle (ADR-0012). The default bare type name
/// made <c>Clinical.ClinicDto</c> and <c>Dtos.AdminManagement.ClinicDto</c> share the <c>ClinicDto</c>
/// schema id, which made <c>/swagger/v1/swagger.json</c> return 500.
/// </summary>
public sealed class SwaggerSchemaIdTests
{
    [Fact]
    public void For_SameNameInDifferentNamespaces_ReturnsDifferentIds()
    {
        var publicSearch = SwaggerSchemaId.For(typeof(ClinicDto));
        var admin = SwaggerSchemaId.For(typeof(AdminClinicDto));

        Assert.NotEqual(publicSearch, admin);
        Assert.Equal("MediPassPlus_Application_Clinical_ClinicDto", publicSearch);
        Assert.Equal("MediPassPlus_Application_Dtos_AdminManagement_ClinicDto", admin);
    }

    [Fact]
    public void For_ClosedGeneric_IncludesTheArgumentNamespace()
    {
        var publicSearch = SwaggerSchemaId.For(typeof(PagedResult<ClinicDto>));

        Assert.Equal(
            "MediPassPlus_Application_Common_PagedResultOf_MediPassPlus_Application_Clinical_ClinicDto",
            publicSearch);
        Assert.NotEqual(publicSearch, SwaggerSchemaId.For(typeof(PagedResult<AdminClinicDto>)));
    }

    [Fact]
    public void For_NestedTypesWithTheSameName_ReturnsDifferentIds()
    {
        var first = SwaggerSchemaId.For(typeof(First.Payload));
        var second = SwaggerSchemaId.For(typeof(Second.Payload));

        Assert.NotEqual(first, second);
        Assert.Contains("_First_Payload", first);
        Assert.Contains("_Second_Payload", second);
    }

    [Fact]
    public void For_EveryPublicTypeInTheSolution_ReturnsAUniqueId()
    {
        var assemblies = new[]
        {
            typeof(ClinicDto).Assembly,
            typeof(MediPassPlus.Domain.Clinical.Clinic).Assembly,
            typeof(Program).Assembly
        };

        var ids = assemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Select(SwaggerSchemaId.For)
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static class First
    {
        public sealed class Payload
        {
        }
    }

    private static class Second
    {
        public sealed class Payload
        {
        }
    }
}

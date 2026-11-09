using System.Reflection;
using System.Security.Claims;
using MediPassPlus.Api.Controllers;
using MediPassPlus.Application.AdminManagement;
using MediPassPlus.Application.Dtos.AdminManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
namespace MediPassPlus.Api.UnitTests;

public class AdminControllersTests
{
    private static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TargetId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Type[] AdminControllers =
    [
        typeof(AdminClinicsController),
        typeof(AdminAccountsController),
        typeof(AdminUsersController)
    ];

    // ---------- Authorization: only Admin ----------

    [Theory]
    [InlineData(typeof(AdminClinicsController))]
    [InlineData(typeof(AdminAccountsController))]
    [InlineData(typeof(AdminUsersController))]
    public void EveryAdminController_RequiresTheAdminRole(Type controllerType)
    {
        var authorize = controllerType.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Admin", authorize.Roles);
    }

    [Fact]
    public void NoAdminEndpoint_AllowsAnonymousAccess()
    {
        foreach (var controller in AdminControllers)
        {
            Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());

            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
            }
        }
    }

    // ---------- Admin has no medical or patient-search routes ----------

    [Fact]
    public void AdminControllers_ExposeNoMedicalOrPatientRoutes()
    {
        var forbiddenWords = new[] { "record", "diagnos", "prescription", "report", "patient" };
        var templates = new List<string>();

        foreach (var controller in AdminControllers)
        {
            templates.Add(controller.GetCustomAttribute<RouteAttribute>()!.Template);

            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var http = method.GetCustomAttribute<HttpMethodAttribute>();
                if (http?.Template is not null)
                {
                    templates.Add(http.Template);
                }
            }
        }

        foreach (var template in templates)
        {
            foreach (var word in forbiddenWords)
            {
                Assert.DoesNotContain(word, template, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // ---------- Clinics ----------

    [Fact]
    public async Task CreateClinic_Returns201WithTheClinic()
    {
        var controller = new AdminClinicsController(new FakeClinicService());

        var result = await controller.Create(new CreateClinicDto("C", "A", null), CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.IsType<ClinicDto>(created.Value);
    }

    [Fact]
    public async Task ListClinics_Returns200()
    {
        var controller = new AdminClinicsController(new FakeClinicService());

        var result = await controller.List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
    }

    [Fact]
    public async Task UpdateClinic_WhenMissing_Returns404()
    {
        var controller = new AdminClinicsController(new FakeClinicService { UpdateReturnsNull = true });

        var result = await controller.Update(999, new UpdateClinicDto("C", "A", null), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task UpdateClinic_WhenFound_Returns200()
    {
        var controller = new AdminClinicsController(new FakeClinicService());

        var result = await controller.Update(1, new UpdateClinicDto("C", "A", null), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    // ---------- Create doctor / staff ----------

    [Fact]
    public async Task CreateDoctor_OnSuccess_Returns201()
    {
        var account = new CreatedAccountDto(TargetId, "Dr", "d@x.test", "Doctor", 1, 1);
        var controller = new AdminAccountsController(
            new FakeAccountService { Result = new CreateAccountResult(account, CreateAccountFailure.None, []) });

        var result = await controller.CreateDoctor(
            new CreateDoctorDto("Dr", "d@x.test", "Demo@12345678", "Neuro", 1), CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result);
        Assert.Same(account, created.Value);
    }

    [Theory]
    [InlineData(CreateAccountFailure.ClinicNotFound, StatusCodes.Status404NotFound)]
    [InlineData(CreateAccountFailure.EmailAlreadyUsed, StatusCodes.Status409Conflict)]
    [InlineData(CreateAccountFailure.IdentityError, StatusCodes.Status400BadRequest)]
    public async Task CreateStaff_OnFailure_MapsToTheRightStatus(CreateAccountFailure failure, int expectedStatus)
    {
        var controller = new AdminAccountsController(
            new FakeAccountService { Result = new CreateAccountResult(null, failure, ["error"]) });

        var result = await controller.CreateStaff(
            new CreateStaffDto("S", "s@x.test", "Demo@12345678", 1), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
    }

    // ---------- Deactivate / reactivate ----------

    [Fact]
    public async Task Deactivate_OnSuccess_Returns204AndPassesFalse()
    {
        var status = new FakeStatusService();
        var controller = CreateUsersController(status);

        var result = await controller.Deactivate(TargetId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.False(status.LastIsActive);
        Assert.Equal(TargetId, status.LastTargetId);
        Assert.Equal(AdminId, status.LastAdminId);
    }

    [Fact]
    public async Task Reactivate_OnSuccess_Returns204AndPassesTrue()
    {
        var status = new FakeStatusService();
        var controller = CreateUsersController(status);

        var result = await controller.Reactivate(TargetId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.True(status.LastIsActive);
    }

    [Theory]
    [InlineData(ChangeStatusFailure.UserNotFound, StatusCodes.Status404NotFound)]
    [InlineData(ChangeStatusFailure.CannotChangeSelf, StatusCodes.Status400BadRequest)]
    public async Task Deactivate_OnFailure_MapsToTheRightStatus(ChangeStatusFailure failure, int expectedStatus)
    {
        var controller = CreateUsersController(new FakeStatusService { Result = failure });

        var result = await controller.Deactivate(TargetId, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
    }

    [Fact]
    public async Task Deactivate_WithoutAUserIdentifier_Returns401()
    {
        var status = new FakeStatusService();
        var controller = CreateUsersController(status, authenticated: false);

        var result = await controller.Deactivate(TargetId, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objectResult.StatusCode);
        Assert.False(status.WasCalled);
    }

    // ---------- Helpers and fakes ----------

    private static AdminUsersController CreateUsersController(FakeStatusService service, bool authenticated = true)
    {
        var context = new DefaultHttpContext();
        if (authenticated)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, AdminId.ToString())],
                "Test"));
        }

        return new AdminUsersController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private sealed class FakeClinicService : IClinicAdminService
    {
        public bool UpdateReturnsNull { get; set; }

        public Task<ClinicDto> CreateAsync(CreateClinicDto request, CancellationToken cancellationToken) =>
            Task.FromResult(new ClinicDto(1, request.Name, request.Address, request.Phone));

        public Task<IReadOnlyList<ClinicDto>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClinicDto>>([new ClinicDto(1, "C", "A", null)]);

        public Task<ClinicDto?> UpdateAsync(int id, UpdateClinicDto request, CancellationToken cancellationToken) =>
            Task.FromResult<ClinicDto?>(UpdateReturnsNull ? null : new ClinicDto(id, request.Name, request.Address, request.Phone));
    }

    private sealed class FakeAccountService : IAccountAdminService
    {
        public CreateAccountResult Result { get; set; } =
            new(null, CreateAccountFailure.IdentityError, ["error"]);

        public Task<CreateAccountResult> CreateDoctorAsync(CreateDoctorDto request, CancellationToken cancellationToken) =>
            Task.FromResult(Result);

        public Task<CreateAccountResult> CreateStaffAsync(CreateStaffDto request, CancellationToken cancellationToken) =>
            Task.FromResult(Result);
    }

    private sealed class FakeStatusService : IAccountStatusService
    {
        public ChangeStatusFailure Result { get; set; } = ChangeStatusFailure.None;
        public bool WasCalled { get; private set; }
        public bool LastIsActive { get; private set; }
        public Guid LastTargetId { get; private set; }
        public Guid LastAdminId { get; private set; }

        public Task<ChangeStatusFailure> SetActiveAsync(
            Guid targetUserId, bool isActive, Guid currentAdminId, CancellationToken cancellationToken)
        {
            WasCalled = true;
            LastIsActive = isActive;
            LastTargetId = targetUserId;
            LastAdminId = currentAdminId;
            return Task.FromResult(Result);
        }
    }
}
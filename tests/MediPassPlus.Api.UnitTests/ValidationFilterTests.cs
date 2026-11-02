using FluentValidation;
using MediPassPlus.Api.Validation;
using MediPassPlus.Application.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MediPassPlus.Api.UnitTests;

public class ValidationFilterTests
{
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddValidatorsFromAssembly(typeof(SampleRequestValidator).Assembly);
        return services.BuildServiceProvider();
    }

    private static ActionExecutingContext CreateContext(ServiceProvider services, object? argument)
    {
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ControllerActionDescriptor());

        return new ActionExecutingContext(
            actionContext,
            [],
            new Dictionary<string, object?> { ["request"] = argument },
            new object());
    }

    private static ActionExecutionDelegate Next(ActionExecutingContext context, Action onCalled)
    {
        return () =>
        {
            onCalled();
            var actionContext = new ActionContext(context.HttpContext, context.RouteData, context.ActionDescriptor);
            return Task.FromResult(new ActionExecutedContext(actionContext, [], new object()));
        };
    }

    [Fact]
    public async Task Invalid_argument_returns_400_validation_problem_details()
    {
        using var services = BuildServices();
        var context = CreateContext(services, new SampleRequest(null, "not-an-email"));
        var filter = new ValidationFilter();
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, Next(context, () => nextCalled = true));

        Assert.False(nextCalled);

        var result = Assert.IsType<BadRequestObjectResult>(context.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(result.Value);

        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("One or more validation errors occurred.", problem.Title);
        Assert.Contains("Name", problem.Errors.Keys);
        Assert.Contains("Email", problem.Errors.Keys);
        Assert.True(problem.Extensions.ContainsKey("traceId"));
    }

    [Fact]
    public async Task Valid_argument_continues_the_pipeline()
    {
        using var services = BuildServices();
        var context = CreateContext(services, new SampleRequest("Arwa", "arwa@example.com"));
        var filter = new ValidationFilter();
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, Next(context, () => nextCalled = true));

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Argument_without_validator_continues_the_pipeline()
    {
        using var services = BuildServices();
        var context = CreateContext(services, new object());
        var filter = new ValidationFilter();
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, Next(context, () => nextCalled = true));

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }
}

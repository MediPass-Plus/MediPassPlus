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

    private static ActionExecutingContext CreateContext(
        ServiceProvider services,
        Type parameterType,
        object? argument,
        bool includeArgument = true)
    {
        var httpContext = new DefaultHttpContext { RequestServices = services };

        var descriptor = new ControllerActionDescriptor
        {
            Parameters =
            [
                new ParameterDescriptor { Name = "request", ParameterType = parameterType }
            ]
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);

        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (includeArgument)
        {
            arguments["request"] = argument;
        }

        return new ActionExecutingContext(actionContext, [], arguments, new object());
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
        var context = CreateContext(services, typeof(SampleRequest), new SampleRequest(null, "not-an-email"));
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
        var context = CreateContext(services, typeof(SampleRequest), new SampleRequest("Arwa", "arwa@example.com"));
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
        var context = CreateContext(services, typeof(object), new object());
        var filter = new ValidationFilter();
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, Next(context, () => nextCalled = true));

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Null_argument_with_validator_returns_400_and_does_not_reach_the_action()
    {
        using var services = BuildServices();
        var context = CreateContext(services, typeof(SampleRequest), argument: null);
        var filter = new ValidationFilter();
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, Next(context, () => nextCalled = true));

        Assert.False(nextCalled);

        var result = Assert.IsType<BadRequestObjectResult>(context.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(result.Value);

        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Contains("request", problem.Errors.Keys);
        Assert.Contains("must not be empty", problem.Errors["request"][0]);
    }

    [Fact]
    public async Task Missing_argument_with_validator_returns_400_and_does_not_reach_the_action()
    {
        using var services = BuildServices();
        var context = CreateContext(services, typeof(SampleRequest), argument: null, includeArgument: false);
        var filter = new ValidationFilter();
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, Next(context, () => nextCalled = true));

        Assert.False(nextCalled);
        Assert.IsType<BadRequestObjectResult>(context.Result);
    }
}

using MediPassPlus.Api.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediPassPlus.Api.UnitTests;

public class ApiExceptionHandlerTests
{
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Returns_500_problem_details_without_exception_details()
    {
        using var services = BuildServices();

        var handler = new ApiExceptionHandler(
            services.GetRequiredService<IProblemDetailsService>(),
            services.GetRequiredService<ILogger<ApiExceptionHandler>>());

        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(
            httpContext,
            new InvalidOperationException("internal detail that must not leak"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);

        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body);
        var body = await reader.ReadToEndAsync();

        Assert.Contains("An unexpected error occurred.", body);
        Assert.DoesNotContain("internal detail that must not leak", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("   at ", body);
    }
}

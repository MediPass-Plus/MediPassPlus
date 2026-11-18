using MediPassPlus.Api.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
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
            services.GetRequiredService<ILogger<ApiExceptionHandler>>(),
            new StubHostEnvironment("Testing"));

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

    [Fact]
    public async Task OutsideDevelopment_LogsTheTypeAndStackTraceButNotTheMessage()
    {
        // FR-PRV-08: an exception message can carry a patient name, a connection string or a parameter value. The
        // name below is synthetic — a canary, not a real person — so a leak is unambiguous when it appears.
        using var services = BuildServices();
        var logger = new CapturingLogger();

        var handler = new ApiExceptionHandler(
            services.GetRequiredService<IProblemDetailsService>(),
            logger,
            new StubHostEnvironment("Testing"));

        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Response.Body = new MemoryStream();

        await handler.TryHandleAsync(
            httpContext,
            new InvalidOperationException("Patient Nagham Ahmed could not be loaded."),
            CancellationToken.None);

        var logged = string.Join(Environment.NewLine, logger.Messages);
        Assert.Contains("System.InvalidOperationException", logged);
        Assert.DoesNotContain("Nagham Ahmed", logged);

        // The exception object itself is not handed to the provider, so a console formatter cannot render
        // exception.ToString() (message and stack trace) behind our back.
        Assert.All(logger.Exceptions, exception => Assert.Null(exception));
    }

    [Fact]
    public async Task InDevelopment_LogsTheWholeException()
    {
        using var services = BuildServices();
        var logger = new CapturingLogger();

        var handler = new ApiExceptionHandler(
            services.GetRequiredService<IProblemDetailsService>(),
            logger,
            new StubHostEnvironment("Development"));

        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Response.Body = new MemoryStream();

        await handler.TryHandleAsync(
            httpContext,
            new InvalidOperationException("development detail"),
            CancellationToken.None);

        var exception = Assert.Single(logger.Exceptions);
        Assert.NotNull(exception);
        Assert.Contains("development detail", exception!.ToString(), StringComparison.Ordinal);
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "MediPassPlus.Api.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CapturingLogger : ILogger<ApiExceptionHandler>
    {
        public List<string> Messages { get; } = new();

        public List<Exception?> Exceptions { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}

using MediPassPlus.Api.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MediPassPlus.Api.UnitTests.Logging;

/// <summary>
/// Proves the correlation id is resolved safely, echoed in the response, stored for the request and opened as a log
/// scope (NFR-REL-02).
/// </summary>
public sealed class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task Invoke_WithoutAHeader_GeneratesA32CharacterIdAndContinues()
    {
        var logger = new RecordingLogger<CorrelationIdMiddleware>();
        var context = new DefaultHttpContext();
        var called = false;

        var middleware = new CorrelationIdMiddleware(
            _ =>
            {
                called = true;
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(context);

        Assert.True(called);

        var id = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.Matches("^[0-9a-f]{32}$", id);
        Assert.Equal(id, context.Items[CorrelationIdMiddleware.ItemKey]);
        Assert.Equal(id, logger.CorrelationId);
    }

    [Fact]
    public async Task Invoke_WithAValidHeader_EchoesItUnchanged()
    {
        var logger = new RecordingLogger<CorrelationIdMiddleware>();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "abc-123.DEF_9";

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context);

        Assert.Equal("abc-123.DEF_9", context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
        Assert.Equal("abc-123.DEF_9", context.Items[CorrelationIdMiddleware.ItemKey]);
        Assert.Equal("abc-123.DEF_9", logger.CorrelationId);
    }

    [Fact]
    public async Task Invoke_WithAnOverlongHeader_ReplacesIt()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = new string('a', 65);

        await new CorrelationIdMiddleware(_ => Task.CompletedTask, new RecordingLogger<CorrelationIdMiddleware>())
            .InvokeAsync(context);

        Assert.Matches(
            "^[0-9a-f]{32}$",
            context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task Invoke_WithControlCharacters_ReplacesIt()
    {
        // An unvalidated header would let a client inject a fake log line (log injection).
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "abc\nINFO fake entry";

        await new CorrelationIdMiddleware(_ => Task.CompletedTask, new RecordingLogger<CorrelationIdMiddleware>())
            .InvokeAsync(context);

        var id = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.Matches("^[0-9a-f]{32}$", id);
        Assert.DoesNotContain("fake", id, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invoke_WithACommaSeparatedHeader_ReplacesIt()
    {
        // Multiple header values arrive joined by a comma; that is not a single id, so it is replaced.
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "first,second";

        await new CorrelationIdMiddleware(_ => Task.CompletedTask, new RecordingLogger<CorrelationIdMiddleware>())
            .InvokeAsync(context);

        Assert.Matches(
            "^[0-9a-f]{32}$",
            context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public string? CorrelationId { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object>> values)
            {
                foreach (var pair in values)
                {
                    if (pair.Key == CorrelationIdMiddleware.ItemKey)
                    {
                        CorrelationId = pair.Value?.ToString();
                    }
                }
            }

            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
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

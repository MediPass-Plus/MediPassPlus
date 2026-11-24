using MediPassPlus.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace MediPassPlus.Api.UnitTests.Security;

public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AddsTheHardeningHeaders()
    {
        var (context, feature) = CreateContext();

        await CreateMiddleware().InvokeAsync(context);
        await feature.StartAsync();

        Assert.Equal("nosniff", feature.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", feature.Headers["X-Frame-Options"].ToString());
        Assert.Equal("no-referrer", feature.Headers["Referrer-Policy"].ToString());
        Assert.Equal("camera=(self)", feature.Headers["Permissions-Policy"].ToString());
        Assert.Equal(SecurityHeadersMiddleware.ContentSecurityPolicy, feature.Headers["Content-Security-Policy"].ToString());
    }

    [Fact]
    public async Task InvokeAsync_DoesNotOverwriteAHeaderTheEndpointSet()
    {
        var (context, feature) = CreateContext();
        feature.Headers["X-Frame-Options"] = "SAMEORIGIN";

        await CreateMiddleware().InvokeAsync(context);
        await feature.StartAsync();

        Assert.Equal("SAMEORIGIN", feature.Headers["X-Frame-Options"].ToString());
    }

    private static SecurityHeadersMiddleware CreateMiddleware() =>
        new(_ => Task.CompletedTask);

    private static (DefaultHttpContext Context, RecordingResponseFeature Feature) CreateContext()
    {
        var feature = new RecordingResponseFeature();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(feature);

        return (context, feature);
    }

    /// <summary>A response feature that runs the <c>OnStarting</c> callbacks when the test starts the response.</summary>
    private sealed class RecordingResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _callbacks = [];

        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state) =>
            _callbacks.Add((callback, state));

        public void OnCompleted(Func<object, Task> callback, object state) =>
            throw new NotSupportedException("The middleware does not register OnCompleted callbacks.");

        public async Task StartAsync()
        {
            HasStarted = true;

            foreach (var (callback, state) in _callbacks)
            {
                await callback(state);
            }
        }
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using MediPassPlus.Api.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediPassPlus.Api.IntegrationTests;

/// <summary>
/// The log redaction review as a test (FR-PRV-08, NFR-REL-02). It runs a real request through the real pipeline,
/// captures every line written while it runs and proves two things: each line carries the correlation id the client
/// sent, and no line repeats the email, the password or the patient name that the request carried.
/// </summary>
/// <remarks>
/// The scenario is a password reset because it is the one flow that sends an email, and the no-op sender writes a
/// warning. That guarantees at least one captured line, so an empty capture cannot make the test pass by accident.
/// The written checklist is in <c>docs/development/logging.md</c>.
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
public sealed class LogRedactionTests
{
    private const string Password = "Redaction!Pass1";
    private const string FullName = "Redaction Review Patient";

    [Fact]
    public async Task APasswordResetRequest_LogsItsIdentifiersOnly()
    {
        var capture = new CapturingLoggerProvider();

        await using var factory = new MediPassApiFactory(
            nameof(LogRedactionTests),
            "Testing",
            exposeApiReference: false,
            configureTestServices: services => services.AddSingleton<ILoggerProvider>(capture));

        using var client = factory.CreateClient();

        var email = $"redaction.{Guid.NewGuid():N}@integration.medipass.test";

        using (var register = await client.PostAsync(
                   "/api/Auth/register",
                   JsonBody(new { fullName = FullName, email, role = "Patient", password = Password })))
        {
            Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        }

        // Only the request under review is captured.
        capture.Clear();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/forgot-password");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "redaction-review-1");
        request.Content = JsonBody(new { email });

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var entries = capture.Entries;
        Assert.NotEmpty(entries);

        // Every line written while the request ran is traceable by the id the client sent.
        Assert.All(entries, entry => Assert.Equal("redaction-review-1", entry.CorrelationId));

        foreach (var entry in entries)
        {
            Assert.DoesNotContain(email, entry.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Password, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(FullName, entry.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bearer ", entry.Message, StringComparison.Ordinal);
        }
    }

    private static StringContent JsonBody(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}

/// <summary>One captured log line: its category, its rendered message and the correlation id active at the time.</summary>
internal sealed record CapturedLogEntry(string Category, string Message, string? CorrelationId);

/// <summary>
/// Captures the application's log lines, including their scopes, so a test can review what the pipeline actually
/// writes. It is registered only through the test factory's hook and never exists in the product host.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<CapturedLogEntry> Entries => _entries.ToArray();

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly CapturingLoggerProvider _owner;
        private readonly string _category;

        public CapturingLogger(CapturingLoggerProvider owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            string? correlationId = null;

            _owner._scopes.ForEachScope(
                (scope, _) =>
                {
                    if (scope is not IEnumerable<KeyValuePair<string, object>> values)
                    {
                        return;
                    }

                    foreach (var pair in values)
                    {
                        if (pair.Key == CorrelationIdMiddleware.ItemKey)
                        {
                            correlationId = pair.Value?.ToString();
                        }
                    }
                },
                (object?)null);

            _owner._entries.Enqueue(
                new CapturedLogEntry(_category, formatter(state, exception), correlationId));
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

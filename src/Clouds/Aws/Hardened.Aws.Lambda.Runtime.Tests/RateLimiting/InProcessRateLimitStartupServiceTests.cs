using Hardened.Aws.Lambda.Runtime.RateLimiting;
using Hardened.Requests.Abstract.RateLimiting;
using Hardened.Requests.Runtime.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Aws.Lambda.Runtime.Tests.RateLimiting;

/// <summary>
/// A function that declares rate limits and counts them per execution environment.
/// </summary>
public class InProcessRateLimitStartupServiceTests
{
    [Fact]
    public async Task LimitedHandlersCountedInProcessWarnAndAreNamed()
    {
        var log = await Run(inProcess: true, "GET /quotes", "POST /quotes");

        var warning = Assert.Single(log.Warnings);

        Assert.Contains("2 handler(s)", warning);
        Assert.Contains("GET /quotes", warning);
        Assert.Contains("POST /quotes", warning);
        Assert.Contains("per execution environment", warning);
    }

    /// <summary>A store of the application's own counts somewhere shared, as the guide says to.</summary>
    [Fact]
    public async Task AStoreOfTheApplicationsOwnIsSilent()
    {
        var log = await Run(inProcess: false, "GET /quotes");

        Assert.Empty(log.Warnings);
    }

    /// <summary>
    /// The ordinary function. The routing generator emits no manifest when no handler is limited.
    /// </summary>
    [Fact]
    public async Task NoLimitedHandlerIsSilent()
    {
        var log = await Run(inProcess: true);

        Assert.Empty(log.Warnings);
    }

    private static async Task<RecordingLoggerProvider> Run(bool inProcess, params string[] handlers)
    {
        var services = new ServiceCollection();
        var log = new RecordingLoggerProvider();

        services.AddLogging(logging => logging.AddProvider(log));
        services.AddSingleton(new RateLimitConfiguration());

        if (inProcess)
        {
            services.AddSingleton<IRateLimitStore, InProcessRateLimitStore>();
        }
        else
        {
            services.AddSingleton<IRateLimitStore, SharedStore>();
        }

        if (handlers.Length > 0)
        {
            services.AddSingleton<IRateLimitManifest>(new Manifest(handlers));
        }

        await new InProcessRateLimitStartupService().Startup(services.BuildServiceProvider());

        return log;
    }

    private sealed class Manifest(params string[] handlers) : IRateLimitManifest
    {
        public IReadOnlyList<string> Handlers { get; } = handlers;
    }

    private sealed class SharedStore : IRateLimitStore
    {
        public ValueTask<RateLimitDecision> Acquire(
            string partition,
            RateLimitPolicy policy,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();
    }

    /// <summary>Keeps the rendered text of every warning, which is what the assertions read.</summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<string> Warnings { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Recording(this);

        public void Dispose() { }

        private sealed class Recording(RecordingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                if (logLevel == LogLevel.Warning)
                {
                    provider.Warnings.Add(formatter(state, exception));
                }
            }
        }
    }
}

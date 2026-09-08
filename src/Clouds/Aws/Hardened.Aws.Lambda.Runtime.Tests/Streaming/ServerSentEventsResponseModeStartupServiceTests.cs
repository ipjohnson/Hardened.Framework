using Hardened.Aws.Lambda.Runtime.Streaming;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hardened.Aws.Lambda.Runtime.Tests.Streaming;

/// <summary>
/// The one thing that can say the response mode and the application's event-stream handlers
/// disagree. The build cannot: the mode is an environment variable and the same assembly serves
/// both modes.
/// </summary>
public class ServerSentEventsResponseModeStartupServiceTests {

    [Fact]
    public async Task BufferedWithEventHandlersWarnsAndNamesThem() {
        var log = await Run(LambdaResponseMode.Buffered, "GET /orders/live", "GET /prices/live");

        var warning = Assert.Single(log.Warnings);

        Assert.Contains(LambdaResponseModeConfiguration.EnvironmentVariable, warning);
        Assert.Contains("GET /orders/live", warning);
        Assert.Contains("GET /prices/live", warning);
        Assert.Contains("RESPONSE_STREAM", warning);
    }

    /// <summary>
    /// The combination the warning exists to catch is the only one it fires on.
    /// </summary>
    [Fact]
    public async Task StreamModeWithEventHandlersIsSilent() {
        var log = await Run(LambdaResponseMode.Stream, "GET /orders/live");

        Assert.Empty(log.Warnings);
    }

    /// <summary>
    /// The ordinary application. The routing generator emits no manifest when no handler is framed
    /// as events, so there is nothing registered and nothing to say.
    /// </summary>
    [Fact]
    public async Task BufferedWithNoEventHandlersIsSilent() {
        var log = await Run(LambdaResponseMode.Buffered);

        Assert.Empty(log.Warnings);
    }

    /// <summary>
    /// Two tables in one application, which is what a library contributing routes looks like.
    /// </summary>
    [Fact]
    public async Task HandlersFromEveryManifestAreNamed() {
        var services = Services(LambdaResponseMode.Buffered);
        var log = new RecordingLoggerProvider();

        services.AddSingleton<IServerSentEventManifest>(new Manifest("GET /orders/live"));
        services.AddSingleton<IServerSentEventManifest>(new Manifest("GET /prices/live"));
        services.AddLogging(logging => logging.AddProvider(log));

        await new ServerSentEventsResponseModeStartupService().Startup(services.BuildServiceProvider());

        var warning = Assert.Single(log.Warnings);

        Assert.Contains("GET /orders/live", warning);
        Assert.Contains("GET /prices/live", warning);
        Assert.Contains("2 handler(s)", warning);
    }

    /// <summary>
    /// Startup does not depend on there being somewhere to log to.
    /// </summary>
    [Fact]
    public async Task AnApplicationWithNoLoggerStillStarts() {
        var services = Services(LambdaResponseMode.Buffered);

        services.AddSingleton<IServerSentEventManifest>(new Manifest("GET /orders/live"));

        Assert.True(
            await new ServerSentEventsResponseModeStartupService().Startup(services.BuildServiceProvider()));
    }

    private static async Task<RecordingLoggerProvider> Run(
        LambdaResponseMode mode, params string[] handlers) {
        var services = Services(mode);
        var log = new RecordingLoggerProvider();

        if (handlers.Length > 0) {
            services.AddSingleton<IServerSentEventManifest>(new Manifest(handlers));
        }

        services.AddLogging(logging => logging.AddProvider(log));

        await new ServerSentEventsResponseModeStartupService().Startup(services.BuildServiceProvider());

        return log;
    }

    private static ServiceCollection Services(LambdaResponseMode mode) {
        var services = new ServiceCollection();

        services.AddSingleton(
            Options.Create<ILambdaResponseModeConfiguration>(
                new LambdaResponseModeConfiguration { Mode = mode }));

        return services;
    }

    private sealed class Manifest(params string[] handlers) : IServerSentEventManifest {
        public IReadOnlyList<string> Handlers { get; } = handlers;
    }

    /// <summary>Keeps the rendered text of every warning, which is what the assertions read.</summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider {
        public List<string> Warnings { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Recording(this);

        public void Dispose() { }

        private sealed class Recording(RecordingLoggerProvider provider) : ILogger {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) {
                if (logLevel == LogLevel.Warning) {
                    provider.Warnings.Add(formatter(state, exception));
                }
            }
        }
    }
}

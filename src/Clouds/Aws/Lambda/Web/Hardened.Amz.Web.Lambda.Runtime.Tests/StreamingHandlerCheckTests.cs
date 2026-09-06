using Hardened.Amz.Shared.Lambda.Runtime.Streaming;
using Hardened.Amz.Web.Lambda.Runtime.Impl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Runtime.Tests;

/// <summary>
/// The startup warning that replaced the build-time refusal of server-sent events on a buffered
/// host: the build names the handlers, the running application knows the mode.
/// </summary>
public class StreamingHandlerCheckTests {

    private sealed class CapturingLoggerProvider : ILoggerProvider {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose() { }

        private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) {
                provider.Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }

    private static (IServiceProvider Services, CapturingLoggerProvider Log) Services(
        LambdaResponseMode? mode, bool withLogging = true) {
        var services = new ServiceCollection();
        var log = new CapturingLoggerProvider();

        if (mode.HasValue) {
            services.AddSingleton(Options.Create<ILambdaResponseModeConfiguration>(
                new LambdaResponseModeConfiguration { Mode = mode.Value }));
        }

        if (withLogging) {
            services.AddLogging(builder => builder.AddProvider(log));
        }

        return (services.BuildServiceProvider(), log);
    }

    /// <summary>
    /// The common case costs nothing: an application with no event-stream handlers does not even
    /// resolve the mode.
    /// </summary>
    [Fact]
    public void AnApplicationWithNoEventStreamHandlersWarnsAboutNothingAndReadsNothing() {
        var (services, log) = Services(mode: null);

        StreamingHandlerCheck.Warn(services, []);

        Assert.Empty(log.Entries);
    }

    [Fact]
    public void StreamModeWarnsAboutNothing() {
        var (services, log) = Services(LambdaResponseMode.Stream);

        StreamingHandlerCheck.Warn(services, ["App.FeedController.Feed"]);

        Assert.Empty(log.Entries);
    }

    [Fact]
    public void BufferedModeWithEventStreamHandlersWarnsNamingThemAndTheSetting() {
        var (services, log) = Services(LambdaResponseMode.Buffered);

        StreamingHandlerCheck.Warn(services, ["App.FeedController.Feed", "App.TickerController.Watch"]);

        var entry = Assert.Single(log.Entries);

        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("App.FeedController.Feed", entry.Message);
        Assert.Contains("App.TickerController.Watch", entry.Message);
        Assert.Contains(LambdaResponseModeConfiguration.EnvironmentVariable, entry.Message);
        Assert.Contains("RESPONSE_STREAM", entry.Message);
    }

    /// <summary>
    /// A container without logging is a container without logging; the check must not be the
    /// thing that fails the application's construction.
    /// </summary>
    [Fact]
    public void AContainerWithoutLoggingIsLeftAlone() {
        var (services, _) = Services(LambdaResponseMode.Buffered, withLogging: false);

        StreamingHandlerCheck.Warn(services, ["App.FeedController.Feed"]);
    }
}

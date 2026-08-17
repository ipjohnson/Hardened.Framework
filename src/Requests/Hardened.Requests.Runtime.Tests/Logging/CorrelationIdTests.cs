using System.Diagnostics;
using Hardened.Requests.Abstract.Diagnostics;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Diagnostics;
using Hardened.Requests.Runtime.Logging;
using Hardened.Requests.Runtime.Middleware;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Logging;

/// <summary>
/// One id per request: what it is, where it survives to, and what happens to it when nothing is
/// collecting traces.
///
/// <para>
/// The last is the whole reason this exists rather than "read the trace id".
/// <c>ActivitySource.StartActivity</c> returns null with no listener, so on a machine with no
/// collector there is no trace id at all - which is exactly when somebody reading logs wants an id
/// to group them by.
/// </para>
/// </summary>
public class CorrelationIdTests {

    /// <summary>
    /// Listens to the pipeline's source so that spans are actually created, since without a
    /// listener <c>StartActivity</c> returns null and there is nothing to read a trace id from.
    /// </summary>
    private static ActivityListener Listening() {
        var listener = new ActivityListener {
            ShouldListenTo = source => source.Name == HardenedDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    // ------------------------------------------------------------ the value

    /// <summary>Always present, whatever else is or is not configured.</summary>
    [Fact]
    public void CorrelationId_IsNeverEmpty() {
        Assert.False(string.IsNullOrEmpty(Pipeline.Context().CorrelationId));
    }

    /// <summary>
    /// Stable across reads. A property that minted a new value each time would put a different id
    /// on every log line of the same request.
    /// </summary>
    [Fact]
    public void CorrelationId_IsTheSameOnEveryRead() {
        var context = Pipeline.Context();

        Assert.Equal(context.CorrelationId, context.CorrelationId);
    }

    /// <summary>Two requests are two ids.</summary>
    [Fact]
    public void CorrelationId_DiffersBetweenRequests() {
        Assert.NotEqual(Pipeline.Context().CorrelationId, Pipeline.Context().CorrelationId);
    }

    /// <summary>
    /// Shaped like a trace id whether or not it came from one, so a log query does not have to
    /// handle two formats.
    /// </summary>
    [Fact]
    public void CorrelationId_IsThirtyTwoHexCharacters() {
        var id = Pipeline.Context().CorrelationId;

        Assert.Equal(32, id.Length);
        Assert.All(id, c => Assert.True(Uri.IsHexDigit(c), $"'{c}' is not hex"));
    }

    /// <summary>
    /// With a collector attached, the correlation id <em>is</em> the trace id - so a log line and
    /// the span for the same request carry the same string and nobody has to join two identifiers.
    /// </summary>
    [Fact]
    public void CorrelationId_IsTheTraceIdWhenSomethingIsTracing() {
        using var listener = Listening();

        var context = Pipeline.Context();
        var logger = new RequestLogger(Pipeline.Logger<RequestLogger>());

        logger.RequestBegin(context);

        var traceId = Activity.Current?.TraceId.ToHexString();

        Assert.NotNull(traceId);
        Assert.Equal(traceId, context.CorrelationId);

        logger.RequestEnd(context);
    }

    /// <summary>
    /// And with nothing listening there is still an id. This is the case the trace id cannot cover.
    /// </summary>
    [Fact]
    public void CorrelationId_IsStillIssuedWhenNothingIsTracing() {
        var context = Pipeline.Context();
        var logger = new RequestLogger(Pipeline.Logger<RequestLogger>());

        logger.RequestBegin(context);

        Assert.False(string.IsNullOrEmpty(context.CorrelationId));

        logger.RequestEnd(context);
    }

    /// <summary>
    /// A fork is the same request. <c>Clone</c> carries the id for the same reason it carries the
    /// caller - a retried or forked chain reporting a second id would split one request's logs.
    /// </summary>
    [Fact]
    public void CorrelationId_SurvivesClone() {
        var context = Pipeline.Context();

        Assert.Equal(context.CorrelationId, context.Clone().CorrelationId);
    }

    /// <summary>Including when the clone replaces the request and response.</summary>
    [Fact]
    public void CorrelationId_SurvivesCloneThatReplacesRequestAndResponse() {
        var context = Pipeline.Context();
        var clone = context.Clone(request: context.Request.Clone(path: "/elsewhere"));

        Assert.Equal(context.CorrelationId, clone.CorrelationId);
    }

    // ------------------------------------------------------------- the scope

    /// <summary>
    /// Every line written while the request runs carries the id, including lines this class knows
    /// nothing about - which is the point of using a scope rather than a message parameter.
    /// </summary>
    [Fact]
    public void RequestBegin_PutsTheCorrelationIdInScopeForEveryLogLine() {
        var provider = new ScopeCapturingProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));

        var context = Pipeline.Context();
        var logger = new RequestLogger(factory.CreateLogger<RequestLogger>());

        logger.RequestBegin(context);

        // Something the framework did not write.
        factory.CreateLogger("Application").LogInformation("charging card");

        logger.RequestEnd(context);

        Assert.Contains(context.CorrelationId, provider.ScopesFor("charging card"));
    }

    /// <summary>
    /// The scope closes when the request ends, so the id does not leak onto whatever the thread
    /// does next.
    /// </summary>
    [Fact]
    public void RequestEnd_ClosesTheScope() {
        var provider = new ScopeCapturingProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));

        var context = Pipeline.Context();
        var logger = new RequestLogger(factory.CreateLogger<RequestLogger>());

        logger.RequestBegin(context);
        logger.RequestEnd(context);

        factory.CreateLogger("Application").LogInformation("after the request");

        Assert.DoesNotContain(context.CorrelationId, provider.ScopesFor("after the request"));
    }

    /// <summary>
    /// Closed on the uninstrumented path too. The end used to return early when there was no span,
    /// and a request with no span is precisely the one that has a scope worth closing.
    /// </summary>
    [Fact]
    public void RequestEnd_ClosesTheScopeEvenWithNoSpan() {
        var provider = new ScopeCapturingProvider();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));

        var context = Pipeline.Context();
        var logger = new RequestLogger(factory.CreateLogger<RequestLogger>());

        // No listener, so StartActivity returns null and no span is stored.
        logger.RequestBegin(context);
        logger.RequestEnd(context);

        factory.CreateLogger("Application").LogInformation("afterwards");

        Assert.DoesNotContain(context.CorrelationId, provider.ScopesFor("afterwards"));
    }

    // ------------------------------------------------------------ the header

    /// <summary>The caller gets the id back, so they can quote it.</summary>
    [Fact]
    public async Task CorrelationHeaderFilter_PutsTheIdOnTheResponse() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, new CorrelationHeaderFilter()).Next();

        Assert.Equal(
            context.CorrelationId,
            context.Response.Headers[CorrelationHeaderFilter.HeaderName].ToString());
    }

    /// <summary>
    /// Set on the way in, so a filter that refuses the request without calling <c>Next</c> - a rate
    /// limiter's 429, a rejected preflight - still comes back with an id. Those are the responses
    /// somebody is most likely to ask about.
    /// </summary>
    [Fact]
    public async Task CorrelationHeaderFilter_SetsTheHeaderEvenWhenTheRequestIsRefused() {
        var context = Pipeline.Context();

        await Pipeline.Chain(
            context,
            new CorrelationHeaderFilter(),
            new Pipeline.Inline(chain => {
                chain.Context.Response.Status = 429;

                return Task.CompletedTask;
            })).Next();

        Assert.Equal(429, context.Response.Status);
        Assert.Equal(
            context.CorrelationId,
            context.Response.Headers[CorrelationHeaderFilter.HeaderName].ToString());
    }

    /// <summary>The header name is configurable for a deployment that already has a convention.</summary>
    [Fact]
    public async Task CorrelationHeaderFilter_HonoursAConfiguredHeaderName() {
        var context = Pipeline.Context();

        await Pipeline.Chain(context, new CorrelationHeaderFilter("X-Request-Id")).Next();

        Assert.Equal(context.CorrelationId, context.Response.Headers["X-Request-Id"].ToString());
    }

    /// <summary>
    /// Seeded into every middleware chain, so no host has to remember - the same reason the
    /// response finalizer is.
    /// </summary>
    [Fact]
    public async Task MiddlewareService_ReturnsTheIdWithoutAnyHostRegisteringTheFilter() {
        var context = Pipeline.Context();
        var service = new MiddlewareService();

        await service.GetExecutionChain(context).Next();

        Assert.Equal(
            context.CorrelationId,
            context.Response.Headers[CorrelationHeaderFilter.HeaderName].ToString());
    }

    // ----------------------------------------------------------- the generator

    /// <summary>
    /// Outside any activity the generator issues a fresh id rather than the all-zero trace id an
    /// absent activity would otherwise imply.
    /// </summary>
    [Fact]
    public void ForCurrentTrace_DoesNotIssueTheZeroTraceId() {
        var zero = default(ActivityTraceId).ToHexString();

        Assert.NotEqual(zero, CorrelationIdentifier.ForCurrentTrace());
    }

    /// <summary>Captures the scopes in force when each message was written.</summary>
    private sealed class ScopeCapturingProvider : ILoggerProvider {
        private readonly List<(string Message, List<string> Scopes)> _written = new();
        private readonly AsyncLocal<Stack<object>> _scopes = new();

        public IEnumerable<string> ScopesFor(string message) =>
            _written.Where(w => w.Message == message).SelectMany(w => w.Scopes);

        public ILogger CreateLogger(string categoryName) => new Capturing(this);

        public void Dispose() { }

        private Stack<object> Current => _scopes.Value ??= new Stack<object>();

        private sealed class Capturing : ILogger {
            private readonly ScopeCapturingProvider _provider;

            public Capturing(ScopeCapturingProvider provider) {
                _provider = provider;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull {
                _provider.Current.Push(state);

                return new Pop(_provider);
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) {
                var scopes = _provider.Current
                    .SelectMany(Values)
                    .ToList();

                _provider._written.Add((formatter(state, exception), scopes));
            }

            private static IEnumerable<string> Values(object scope) =>
                scope is IReadOnlyList<KeyValuePair<string, object>> pairs
                    ? pairs.Select(p => p.Value?.ToString() ?? "")
                    : new[] { scope.ToString() ?? "" };

            private sealed class Pop : IDisposable {
                private readonly ScopeCapturingProvider _provider;

                public Pop(ScopeCapturingProvider provider) {
                    _provider = provider;
                }

                public void Dispose() {
                    if (_provider.Current.Count > 0) {
                        _provider.Current.Pop();
                    }
                }
            }
        }
    }
}

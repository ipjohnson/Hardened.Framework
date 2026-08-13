using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Metrics;
using Hardened.Web.Kestrel.Runtime.Impl;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Hardened.Web.Kestrel.Runtime.Tests.Impl;

/// <summary>
/// The server-to-application contract: what happens across <c>CreateContext</c>,
/// <c>ProcessRequestAsync</c> and <c>DisposeContext</c>.
///
/// These are the responsibilities <c>HostingApplication</c> carries for ASP.NET and this class
/// carries here — the per-request scope, request logging, the duration metric, and turning an
/// escaped exception into a response rather than a dropped connection.
/// </summary>
public class HardenedHttpApplicationTests {

    [Fact]
    public void CreateContext_BuildsAnExecutionContextFromTheServerFeatures() {
        var harness = new Harness();
        var features = new ServerFeatures("POST", "/orders", "?page=2");

        var context = harness.Application.CreateContext(features.Collection);

        Assert.Equal("POST", context.Execution.Request.Method);
        Assert.Equal("/orders", context.Execution.Request.Path);
        Assert.Equal("2", context.Execution.Request.QueryString.Get("page"));
    }

    [Fact]
    public void CreateContext_LogsTheRequestBeginning() {
        var harness = new Harness();

        harness.Application.CreateContext(new ServerFeatures().Collection);

        harness.RequestLogger.Received(1).RequestBegin(Arg.Any<IExecutionContext>());
    }

    /// <summary>
    /// Without this a client disconnect never reaches the handler and the application keeps
    /// working on a response nobody will read.
    /// </summary>
    [Fact]
    public void CreateContext_TakesItsCancellationTokenFromTheRequestLifetime() {
        var harness = new Harness();
        var features = new ServerFeatures();

        var context = harness.Application.CreateContext(features.Collection);

        Assert.False(context.Execution.CancellationToken.IsCancellationRequested);

        features.Aborted.Cancel();

        Assert.True(context.Execution.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task ProcessRequestAsync_RunsTheMiddlewareChain() {
        var harness = new Harness();

        await harness.Application.ProcessRequestAsync(
            harness.Application.CreateContext(new ServerFeatures().Collection));

        await harness.Chain.Received(1).Next();
    }

    [Fact]
    public async Task ProcessRequestAsync_CompletesTheResponse() {
        var harness = new Harness();
        var features = new ServerFeatures();

        await harness.Application.ProcessRequestAsync(
            harness.Application.CreateContext(features.Collection));

        Assert.Equal(1, features.ResponseBody.CompleteCount);
    }

    /// <summary>
    /// Kestrel does handle an application that throws, but it treats the request as failed: it
    /// logs against the server rather than the application's own logger, and aborts the
    /// connection once the response has started. Catching here keeps the failure in Hardened's
    /// logger and still sends a 500.
    /// </summary>
    [Fact]
    public async Task ProcessRequestAsync_SendsFiveHundredWhenTheChainThrows() {
        var harness = new Harness();
        var features = new ServerFeatures();
        harness.Chain.Next().Throws(new InvalidOperationException("boom"));

        await harness.Application.ProcessRequestAsync(
            harness.Application.CreateContext(features.Collection));

        Assert.Equal(500, features.Response.StatusCode);
        harness.RequestLogger.Received(1).RequestFailed(
            Arg.Any<IExecutionContext>(), Arg.Any<InvalidOperationException>());
    }

    [Fact]
    public async Task ProcessRequestAsync_CompletesTheResponseEvenWhenTheChainThrows() {
        var harness = new Harness();
        var features = new ServerFeatures();
        harness.Chain.Next().Throws(new InvalidOperationException("boom"));

        await harness.Application.ProcessRequestAsync(
            harness.Application.CreateContext(features.Collection));

        Assert.Equal(1, features.ResponseBody.CompleteCount);
    }

    /// <summary>
    /// Once the status line is on the wire there is nothing left to say, and writing a status
    /// after the headers have gone out is an error rather than a correction.
    /// </summary>
    [Fact]
    public async Task ProcessRequestAsync_LeavesTheStatusAloneWhenTheResponseHasAlreadyStarted() {
        var harness = new Harness();
        var features = new ServerFeatures();
        features.Response.StatusCode = 200;
        harness.Chain.Next().Returns(_ => {
            features.Response.HasStarted = true;
            throw new InvalidOperationException("boom");
        });

        await harness.Application.ProcessRequestAsync(
            harness.Application.CreateContext(features.Collection));

        Assert.Equal(200, features.Response.StatusCode);
    }

    [Fact]
    public void DisposeContext_LogsTheRequestEndingAndRecordsItsDuration() {
        var harness = new Harness();
        var context = harness.Application.CreateContext(new ServerFeatures().Collection);

        harness.Application.DisposeContext(context, null);

        harness.RequestLogger.Received(1).RequestEnd(Arg.Any<IExecutionContext>());
        harness.MetricLogger.Received(1).Record(
            RequestMetrics.TotalRequestDuration, Arg.Any<double>());
    }

    /// <summary>
    /// The per-request scope is the application's, so nothing else will dispose it. A leak here
    /// would show up as scoped services accumulating for the life of the process.
    /// </summary>
    [Fact]
    public void DisposeContext_DisposesThePerRequestScope() {
        var harness = new Harness();
        var context = harness.Application.CreateContext(new ServerFeatures().Collection);

        var scoped = context.Scope.ServiceProvider.GetRequiredService<TrackedScopedService>();
        Assert.False(scoped.Disposed);

        harness.Application.DisposeContext(context, null);

        Assert.True(scoped.Disposed);
    }

    public sealed class TrackedScopedService : IDisposable {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private class Harness {
        public Harness() {
            RequestLogger = Substitute.For<IRequestLogger>();
            MetricLogger = Substitute.For<IMetricLogger>();
            Chain = Substitute.For<IExecutionChain>();
            Chain.Next().Returns(Task.CompletedTask);

            var metricLoggerProvider = Substitute.For<IMetricLoggerProvider>();
            metricLoggerProvider.CreateLogger(Arg.Any<string>()).Returns(MetricLogger);

            var middlewareService = Substitute.For<IMiddlewareService>();
            middlewareService.GetExecutionChain(Arg.Any<IExecutionContext>()).Returns(Chain);

            // A real provider rather than a substitute: the application opens a scope from it, and
            // scope disposal is one of the things under test.
            var services = new ServiceCollection();
            services.AddSingleton(Substitute.For<IKnownServices>());
            services.AddScoped<TrackedScopedService>();

            Application = new HardenedHttpApplication(
                services.BuildServiceProvider(), middlewareService, metricLoggerProvider, RequestLogger);
        }

        public IRequestLogger RequestLogger { get; }

        public IMetricLogger MetricLogger { get; }

        public IExecutionChain Chain { get; }

        public HardenedHttpApplication Application { get; }
    }
}

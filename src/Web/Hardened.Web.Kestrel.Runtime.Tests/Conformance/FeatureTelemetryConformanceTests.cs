using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Logging;
using Hardened.Requests.Testing.Conformance;
using Hardened.Shared.Runtime.Metrics;
using Hardened.Web.Kestrel.Runtime.Impl;
using Hardened.Web.Kestrel.Runtime.Tests.Impl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Hardened.Web.Kestrel.Runtime.Tests.Conformance;

/// <summary>
/// Kestrel's half of <see cref="RequestTelemetryConformanceTests"/>.
/// </summary>
public class FeatureTelemetryConformanceTests : RequestTelemetryConformanceTests {
    protected override IRequestTelemetryConformanceAdapter Adapter { get; } = new KestrelAdapter();

    private sealed class KestrelAdapter : IRequestTelemetryConformanceAdapter {
        public string TransportName => "Kestrel";

        /// <summary>
        /// The whole three-step contract Kestrel drives: create, process, dispose.
        /// </summary>
        /// <remarks>
        /// A real <see cref="RequestLogger"/>, unlike the substitute the rest of this project's
        /// harness uses — the suite is asserting what an observer of the real one sees. The
        /// middleware is still a substitute, because what is under test is whether the host reports
        /// the request at all, not what a handler does inside it.
        /// </remarks>
        public async Task Dispatch(TelemetryConformanceRequest request) {
            IExecutionContext? executionContext = null;

            var chain = Substitute.For<IExecutionChain>();
            chain.Next().Returns(_ => {
                request.Handler?.Invoke(executionContext!);

                return Task.CompletedTask;
            });

            var middlewareService = Substitute.For<IMiddlewareService>();
            middlewareService.GetExecutionChain(Arg.Any<IExecutionContext>()).Returns(callInfo => {
                executionContext = callInfo.Arg<IExecutionContext>();

                return chain;
            });

            var services = new ServiceCollection();
            services.AddSingleton(Substitute.For<IKnownServices>());

            var application = new HardenedHttpApplication(
                services.BuildServiceProvider(),
                middlewareService,
                new NullMetricLoggerProvider(),
                new RequestLogger(NullLogger<RequestLogger>.Instance));

            var features = new ServerFeatures(request.Method, request.Path);

            foreach (var header in request.Headers) {
                features.Request.Headers[header.Key] = header.Value;
            }

            var context = application.CreateContext(features.Collection);

            await application.ProcessRequestAsync(context);

            application.DisposeContext(context, null);
        }
    }
}

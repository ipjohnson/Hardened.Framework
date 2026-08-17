using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Logging;
using Hardened.Requests.Testing.Conformance;
using Hardened.Shared.Runtime.Metrics;
using Hardened.Web.AspNetCore.Runtime.Impl;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Hardened.Web.AspNetCore.Runtime.Tests.Conformance;

/// <summary>
/// ASP.NET's half of <see cref="RequestTelemetryConformanceTests"/>.
/// </summary>
public class AspNetTelemetryConformanceTests : RequestTelemetryConformanceTests {
    protected override IRequestTelemetryConformanceAdapter Adapter { get; } = new AspNetAdapter();

    private sealed class AspNetAdapter : IRequestTelemetryConformanceAdapter {
        public string TransportName => "ASP.NET Core";

        /// <summary>
        /// One trip through <see cref="AspNetCoreRequestHandler.HandleRequest"/>, which is the whole
        /// of this host's request lifecycle.
        /// </summary>
        /// <remarks>
        /// The terminal delegate is a no-op. A chain that answers nothing falls through to the rest of
        /// the ASP.NET pipeline, which is deliberate here: the suite is asserting that the request is
        /// reported either way, and a host that only reported the ones it handled itself would be
        /// wrong in exactly the way that is hard to notice.
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

            var handler = new AspNetCoreRequestHandler(
                new NullMetricLoggerProvider(),
                middlewareService,
                new RequestLogger(NullLogger<RequestLogger>.Instance));

            await handler.HandleRequest(HttpContextFor(request), _ => Task.CompletedTask);
        }

        /// <summary>
        /// Built here rather than through the project's other helper, which fixes the method and path
        /// it produces — the suite needs a distinct path per request so its listener can tell one
        /// from another while the rest of the assembly runs in parallel.
        /// </summary>
        private static HttpContext HttpContextFor(TelemetryConformanceRequest request) {
            var requestFeature = new HttpRequestFeature {
                Method = request.Method,
                Path = request.Path
            };

            foreach (var header in request.Headers) {
                requestFeature.Headers[header.Key] = header.Value;
            }

            var features = new FeatureCollection();
            features.Set<IHttpRequestFeature>(requestFeature);
            features.Set<IHttpResponseFeature>(new HttpResponseFeature());
            features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));

            // AspNetExecutionContext resolves IKnownServices out of RequestServices as it is built.
            var services = new ServiceCollection();
            services.AddSingleton(Substitute.For<IKnownServices>());

            return new DefaultHttpContext(features) {
                RequestServices = services.BuildServiceProvider()
            };
        }
    }
}

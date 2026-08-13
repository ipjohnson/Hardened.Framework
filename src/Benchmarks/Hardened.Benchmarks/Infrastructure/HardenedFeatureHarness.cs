using Hardened.Requests.Abstract.Middleware;
using Hardened.Web.Kestrel.Runtime.Impl;
using Hardened.Web.Runtime.Handlers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Benchmarks.Infrastructure;

/// <summary>
/// Hardened owning the server-to-application contract, as it does on Kestrel.
///
/// This drives the real <c>Hardened.Web.Kestrel.Runtime</c> package rather than a copy of it. That
/// matters more than it might seem: the first version of this harness was a prototype living here,
/// and a benchmark measuring a private reimplementation of shipping code is a benchmark that goes
/// quietly stale the moment the two diverge.
///
/// No Kestrel is started. <c>IHttpApplication</c> takes a feature collection, and the same
/// <see cref="HttpContextFactory.CreateFeatures"/> that feeds the ASP.NET pipelines feeds this
/// one, so all of them are measured against identical inputs. What Kestrel adds on top — sockets,
/// HTTP parsing, framing — is excluded here for the same reason it is excluded everywhere else in
/// this project.
///
/// Note that this path is charged for slightly more than the others: the real
/// <c>HardenedHttpApplication</c> calls <c>IRequestLogger</c> on begin and end and records
/// <c>TotalRequestDuration</c>, neither of which <c>AspNetCoreRequestHandler</c> does. That is a
/// genuine behavioural difference between the two hosts rather than harness overhead, so it is
/// left in.
/// </summary>
public sealed class HardenedFeatureHarness : IPipelineHarness {
    private readonly ServiceProvider _provider;
    private readonly IHttpApplication<HardenedHttpApplication.RequestContext> _application;

    public string Name => "hardened-features";

    public HardenedFeatureHarness() {
        _provider = HardenedAppFactory.BuildProvider();
        HardenedAppFactory.RunStartup(_provider);

        // What KestrelServerRunner.StartAsync does before it begins listening.
        var handler = _provider.GetRequiredService<IWebExecutionHandlerService>();
        _provider.GetRequiredService<IMiddlewareService>().Use(_ => handler);

        _application =
            _provider.GetRequiredService<IHttpApplication<HardenedHttpApplication.RequestContext>>();
    }

    public async Task<int> Execute(RequestScenario scenario, MemoryStream responseBody) {
        var features = HttpContextFactory.CreateFeatures(scenario, responseBody);
        var context = _application.CreateContext(features);

        try {
            await _application.ProcessRequestAsync(context);

            return context.Execution.Response.Status ?? 200;
        }
        finally {
            _application.DisposeContext(context, null);
        }
    }

    public void Dispose() => _provider.Dispose();
}

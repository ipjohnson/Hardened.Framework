using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Hardened.Benchmarks.Infrastructure;

/// <summary>
/// A Hardened routing table on its own, for one of the route-scale applications.
///
/// The table is generated code compiled into the SUT assembly, so there is nothing to build
/// here: the provider is constructed, the registered
/// <c>IWebExecutionRequestHandlerProvider</c> instances are pulled out of it, and matching is a
/// call into them. No middleware chain, no handler invocation, no serialization.
/// </summary>
public sealed class HardenedMatcherHarness : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly IWebExecutionRequestHandlerProvider[] _providers;

    public HardenedMatcherHarness(IDependencyModule application)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.ClearProviders());
        services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl("production"));

        application.PopulateServiceCollection(services);

        _provider = services.BuildServiceProvider();

        foreach (var startupService in _provider.GetServices<IStartupService>())
        {
            startupService.Startup(_provider).GetAwaiter().GetResult();
        }

        _scope = _provider.CreateScope();

        // WebExecutionHandlerService reverses the registration order before walking them, so the
        // same order is used here rather than the raw enumeration order.
        _providers =
        [
            .. _provider.GetServices<IWebExecutionRequestHandlerProvider>().Reverse(),
        ];
    }

    /// <summary>
    /// The match, in the shape <c>WebExecutionHandlerService</c> performs it: each registered
    /// provider in turn until one answers.
    /// </summary>
    public RequestHandlerInfo? Match(IExecutionContext context)
    {
        foreach (var provider in _providers)
        {
            var handler = provider.GetExecutionRequestHandler(context);

            if (handler != null)
            {
                return handler;
            }
        }

        return null;
    }

    public IExecutionContext CreateContext(string method, string path)
    {
        var request = new TestExecutionRequest(
            method,
            path,
            null,
            EmptyQueryStringCollection.Instance
        )
        {
            Headers = new Dictionary<string, StringValues>(),
            Body = Stream.Null,
        };

        var response = new TestExecutionResponse(Stream.Null)
        {
            Headers = new Dictionary<string, StringValues>(),
        };

        return new TestExecutionContext(
            _provider,
            _scope.ServiceProvider,
            _scope.ServiceProvider.GetRequiredService<IKnownServices>(),
            request,
            response,
            CancellationToken.None
        );
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}

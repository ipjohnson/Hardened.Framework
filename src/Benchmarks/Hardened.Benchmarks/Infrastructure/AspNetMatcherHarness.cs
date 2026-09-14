using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Hardened.Benchmarks.Infrastructure;

/// <summary>
/// ASP.NET Core's route matcher on its own, built over a list of templates.
///
/// <para>
/// Endpoints are constructed with <c>RouteEndpointBuilder</c> rather than <c>MapGet</c> so that
/// nothing but the route pattern and the HTTP method reaches the matcher. A minimal API endpoint
/// carries a compiled request delegate, filters and parameter binding metadata, none of which
/// the matcher reads, and all of which would otherwise have to be built for 504 routes before
/// the first measurement.
/// </para>
/// <para>
/// <c>MatcherFactory</c>, <c>Matcher</c> and <c>DfaMatcher</c> are all internal, so getting at
/// them means reflection. It happens once in the constructor and the result is bound to a
/// delegate, so the measured call is a plain virtual dispatch with no reflection in it.
/// </para>
/// </summary>
public sealed class AspNetMatcherHarness : IDisposable
{
    private readonly ServiceProvider _provider;

    public AspNetMatcherHarness(IReadOnlyList<(string Method, string Template)> routes)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.ClearProviders());
        services.AddRouting();

        _provider = services.BuildServiceProvider();

        var endpoints = new List<Endpoint>(routes.Count);

        foreach (var (method, template) in routes)
        {
            var builder = new RouteEndpointBuilder(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse(template),
                order: 0
            )
            {
                DisplayName = method + " " + template,
            };

            builder.Metadata.Add(new HttpMethodMetadata([method]));

            endpoints.Add(builder.Build());
        }

        var routingAssembly = typeof(RouteEndpoint).Assembly;

        var factoryType = routingAssembly.GetType(
            "Microsoft.AspNetCore.Routing.Matching.MatcherFactory"
        )!;

        var factory = _provider.GetRequiredService(factoryType);

        var outerMatcher = factoryType
            .GetMethod("CreateMatcher")!
            .Invoke(factory, [new StaticEndpointDataSource(endpoints)])!;

        // CreateMatcher hands back a DataSourceDependentMatcher, whose MatchAsync is one field
        // read and a virtual call through to the DFA. A real application pays that indirection;
        // this is measuring the state machine rather than the wrapper, so it is unwrapped here
        // and called out in the results.
        var inner =
            outerMatcher
                .GetType()
                .GetProperty("CurrentMatcher", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(outerMatcher) ?? outerMatcher;

        MatcherName = inner.GetType().Name;

        Match = (Func<HttpContext, Task>)
            Delegate.CreateDelegate(
                typeof(Func<HttpContext, Task>),
                inner,
                inner.GetType().GetMethod("MatchAsync", [typeof(HttpContext)])!
            );
    }

    /// <summary>The matcher implementation reached, for the record in a results header.</summary>
    public string MatcherName { get; }

    public Func<HttpContext, Task> Match { get; }

    public static DefaultHttpContext CreateContext(string method, string path)
    {
        var context = new DefaultHttpContext();

        context.Request.Method = method;
        context.Request.Path = path;

        return context;
    }

    public void Dispose() => _provider.Dispose();

    /// <summary>
    /// A fixed endpoint list. <c>EndpointDataSource</c> is the only public seam
    /// <c>MatcherFactory.CreateMatcher</c> accepts, and the change token never fires because the
    /// set cannot change.
    /// </summary>
    private sealed class StaticEndpointDataSource : EndpointDataSource
    {
        public StaticEndpointDataSource(IReadOnlyList<Endpoint> endpoints) => Endpoints = endpoints;

        public override IReadOnlyList<Endpoint> Endpoints { get; }

        public override IChangeToken GetChangeToken() => NullChangeToken.Instance;

        private sealed class NullChangeToken : IChangeToken
        {
            public static readonly NullChangeToken Instance = new();

            public bool HasChanged => false;

            public bool ActiveChangeCallbacks => false;

            public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) =>
                NullDisposable.Instance;

            private sealed class NullDisposable : IDisposable
            {
                public static readonly NullDisposable Instance = new();

                public void Dispose() { }
            }
        }
    }
}

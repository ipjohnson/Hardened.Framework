using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.Web.Kestrel.Testing.Tests;

/// <summary>
/// A container with the Kestrel runtime module and one filter that does what a test says, started
/// through the attribute's host the way <c>[WebTesting]</c> starts it: registered through a
/// factory, built, started over the built provider.
/// </summary>
/// <remarks>
/// A filter rather than a generated routing table, because what is under test is the host's
/// translation in and out and its lifetime, not what a handler does inside it - the footing the
/// pipeline handler's own tests stand on. The handler filter the runner appends sits behind it,
/// so a path the filter passes on is a 404 from the real not-found handler.
/// </remarks>
internal sealed class HostHarness : IAsyncDisposable {
    private readonly ServiceProvider _provider;

    private HostHarness(ServiceProvider provider, ITestHost host) {
        _provider = provider;
        Host = host;
    }

    public ITestHost Host { get; }

    /// <summary>
    /// Every request the filter saw, in order, copied while the request was live: Kestrel's request
    /// features are pooled and reset once the request ends, so the live object reads as empty
    /// afterwards.
    /// </summary>
    public List<Seen> Requests { get; } = new();

    public static async Task<HostHarness> Start(
        Func<IExecutionChain, Task> answer, CancellationToken cancellationToken) {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl("test"));

        new KestrelRuntime().PopulateServiceCollection(services);

        var created = new KestrelHostAttribute().CreateHost(null!, services);

        services.AddSingleton<ITestHost>(_ => created);

        var provider = services.BuildServiceProvider();

        // Resolved rather than used as created, the way [WebTesting] reaches it: the container
        // captures a factory's result for disposal when it is resolved, so this is what makes
        // disposing the container dispose the host.
        var host = provider.GetRequiredService<ITestHost>();
        var harness = new HostHarness(provider, host);

        provider.GetRequiredService<IMiddlewareService>().Use(_ => new Answering(harness, answer));

        await host.StartAsync(provider, cancellationToken);

        return harness;
    }

    /// <summary>Disposes the container, which is what disposes the host under a runner.</summary>
    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    private sealed class Answering : IExecutionFilter {
        private readonly HostHarness _harness;
        private readonly Func<IExecutionChain, Task> _answer;

        public Answering(HostHarness harness, Func<IExecutionChain, Task> answer) {
            _harness = harness;
            _answer = answer;
        }

        public Task Execute(IExecutionChain chain) {
            var request = chain.Context.Request;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in request.Headers) {
                headers[header.Key] = header.Value.ToString();
            }

            lock (_harness.Requests) {
                _harness.Requests.Add(new Seen(request.Method, request.Path, headers));
            }

            return _answer(chain);
        }
    }

    public sealed record Seen(string Method, string Path, IReadOnlyDictionary<string, string> Headers);
}

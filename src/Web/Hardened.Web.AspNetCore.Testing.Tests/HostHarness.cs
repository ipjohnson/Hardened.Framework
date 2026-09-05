using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.Web.AspNetCore.Testing.Tests;

/// <summary>
/// A container with the ASP.NET Core runtime module and one filter that does what a test says,
/// built and started the way the runner does it with <c>[AspNetCoreRuntime]</c> on the test and
/// <c>[assembly: AspNetCoreTesting]</c> in scope: the host created before the container,
/// registered through a factory, the container built by the attribute, the host resolved from it
/// and started over it.
/// </summary>
internal sealed class HostHarness : IAsyncDisposable {
    private readonly IServiceProvider _provider;

    private HostHarness(IServiceProvider provider, ITestHost host) {
        _provider = provider;
        Host = host;
    }

    public ITestHost Host { get; }

    public IServiceProvider Provider => _provider;

    /// <summary>Every request the filter saw, in order, copied while the request was live.</summary>
    public List<Seen> Requests { get; } = new();

    public static async Task<HostHarness> Start(
        Func<IExecutionChain, Task> answer,
        CancellationToken cancellationToken,
        Type? composition = null,
        params Attribute[] attributesInScope) {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl("test"));

        new AspNetCoreRuntime().PopulateServiceCollection(services);

        var attribute = composition == null ? new AspNetCoreTestingAttribute() : new AspNetCoreTestingAttribute(composition);
        var context = new FakeTestMethodContext(attributesInScope);
        var created = attribute.CreateHost(context, services);

        services.AddSingleton<ITestHost>(_ => created);

        var provider = attribute.BuildServiceProvider(context, services);

        // Resolved rather than used as created, the way [WebTesting] reaches it: that is what
        // captures it for disposal with the container.
        var host = provider.GetRequiredService<ITestHost>();
        var harness = new HostHarness(provider, host);

        provider.GetRequiredService<IMiddlewareService>().Use(_ => new Answering(harness, answer));

        await host.StartAsync(provider, cancellationToken);

        return harness;
    }

    /// <summary>Disposes the container, which is what disposes the host under a runner.</summary>
    public async ValueTask DisposeAsync() {
        if (_provider is IAsyncDisposable asyncDisposable) {
            await asyncDisposable.DisposeAsync();
        }
    }

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

    /// <summary>The test method the container is built for: this class's marker method, with the attributes a test says are in scope.</summary>
    private sealed class FakeTestMethodContext : ITestMethodContext {
        public FakeTestMethodContext(IReadOnlyList<Attribute> attributes) {
            Attributes = attributes;
        }

        public MethodInfo Method { get; } = typeof(FakeTestMethodContext).GetMethod(nameof(Marker), BindingFlags.NonPublic | BindingFlags.Static)!;

        public IReadOnlyList<Attribute> Attributes { get; }

        private static void Marker() {
        }
    }
}

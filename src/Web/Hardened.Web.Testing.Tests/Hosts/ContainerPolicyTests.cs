using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Web.Testing.Tests.Hosts;

/// <summary>
/// What each host says about the deployment it stands for.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ITestHost.ContainerPolicy"/> is read by a person rather than by the harness: each host
/// already branches on whether it was handed a container source, and the property is where the
/// arrangement is stated so someone can find it. That makes it exactly the kind of thing that drifts
/// without anything noticing, which is why it is asserted rather than left to the remarks.
/// </para>
/// <para>
/// The two answers are not symmetrical, and the asymmetry is the point. Per-invocation is a claim
/// about a deployment: an execution environment is not promised between invocations. Reused is a
/// limit on what a host can check, because a socket cannot be restarted underneath a client the test
/// already holds, and it says nothing about whether sharing is safe where the application really
/// runs.
/// </para>
/// </remarks>
public class ContainerPolicyTests {

    /// <summary>A host that answers nothing, so the interface's own default is what it reports.</summary>
    private sealed class BareHost : ITestHost {
        public bool IsTerminal => true;

        public Uri BaseAddress => new("http://bare/");

        public Task StartAsync(IServiceProvider provider, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public HttpMessageHandler CreateHandler(TestCredential? credential) =>
            throw new NotSupportedException();

        public Task<TestWebResponse> SendAsync(
            TestHostRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => default;
    }

    /// <summary>
    /// The default is the conservative one: a host that has not thought about it keeps one
    /// container, which is what every host did before there was a choice to make.
    /// </summary>
    [Fact]
    public void AHostThatSaysNothingReusesItsContainer() {
        ITestHost host = new BareHost();

        Assert.Equal(TestContainerPolicy.Reused, host.ContainerPolicy);
    }

    /// <summary>
    /// The pipeline rebuilds, and it is the host the overwhelming majority of tests run under, which
    /// is what puts the strict reading where it costs least.
    /// </summary>
    [Fact]
    public void ThePipelineHostBuildsAContainerPerInvocation() {
        ITestHost host = new PipelineHost(new ServiceCollection().BuildServiceProvider());

        Assert.Equal(TestContainerPolicy.PerInvocation, host.ContainerPolicy);
    }
}

/// <summary>
/// What a host appends to a container it did not compose.
/// </summary>
public class PipelineHostCompositionTests {

    /// <summary>
    /// A host over a container someone else built appends nothing to it.
    /// </summary>
    /// <remarks>
    /// The constructor taking a provider is the harness-built-by-hand route, and it is also what an
    /// application root reaches, having composed its own chain already. Appending routing behind
    /// that would put a second terminal handler in a chain that has one.
    /// </remarks>
    [Fact]
    public async Task AHostOverAnAlreadyComposedContainerAppendsNothing() {
        var services = new ServiceCollection();

        services.AddSingleton<IMiddlewareService, RecordingMiddleware>();

        await using var provider = services.BuildServiceProvider();

        var host = new PipelineHost(provider);

        await host.StartAsync(provider, CancellationToken.None);

        Assert.Equal(
            0,
            ((RecordingMiddleware)provider.GetRequiredService<IMiddlewareService>()).Appended);
    }

    /// <summary>Counts what was appended, and does nothing else.</summary>
    private sealed class RecordingMiddleware : IMiddlewareService {
        public int Appended { get; private set; }

        public void Use(Func<IExecutionContext, IExecutionFilter> middlewareFunc) => Appended++;

        public IExecutionChain GetExecutionChain(IExecutionContext context) =>
            throw new NotSupportedException();
    }
}

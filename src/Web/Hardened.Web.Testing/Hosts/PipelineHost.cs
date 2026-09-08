using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
using DependencyModules.Testing.Attributes;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Web.Testing;

/// <summary>
/// The default host: the pipeline, run in the test's own call with no socket.
/// </summary>
/// <remarks>
/// Fast, and the only host that can report the exception a handler threw as
/// <see cref="TestWebResponse.Failure"/>, because nothing crosses a wire. Declared on a method to
/// opt one test back to it inside a class or an assembly that declared a socket host.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class PipelineHostAttribute : TestHostAttribute {

    /// <remarks>
    /// The handler is appended only when the entry point is a module rather than an application
    /// root, which is the branch <c>[WebTesting]</c> always took; an application root composes
    /// its own chain.
    /// </remarks>
    public override ITestHost CreateHost(ITestMethodContext testMethod, IServiceCollection services) {
        var entryPoint = testMethod.Method.GetTestAttribute<HardenedTestEntryPointAttribute>();

        return new PipelineHost(
            appendHandler: entryPoint != null && !typeof(IApplicationRoot).IsAssignableFrom(entryPoint.EntryPoint));
    }
}

/// <summary>
/// The pipeline as a host: what <see cref="ITestWebApp"/> and <see cref="PipelineHttpMessageHandler"/>
/// always ran, behind the seam a socket host shares.
/// </summary>
public sealed class PipelineHost : ITestHost {
    private readonly bool _appendHandler;
    private IServiceProvider? _provider;
    private ITestContainerSource? _source;
    private bool _started;

    /// <summary>A host over a container that is already built and composed, for a harness built by hand.</summary>
    public PipelineHost(IServiceProvider provider) {
        _provider = provider;
        _appendHandler = false;
    }

    internal PipelineHost(bool appendHandler) {
        _appendHandler = appendHandler;
    }

    public bool IsTerminal => true;

    /// <summary>
    /// A container per request, because nothing here holds a socket that a rebuild would close.
    /// </summary>
    /// <remarks>
    /// The strict reading, and the default one: this host names no cloud, so it does not get to
    /// assume the most forgiving deployment. It is also the host the overwhelming majority of tests
    /// run under, which is what puts the check where it costs least - the two hosts that cannot
    /// rebuild are the ones a suite has few of.
    /// </remarks>
    public TestContainerPolicy ContainerPolicy => TestContainerPolicy.PerInvocation;

    public Uri BaseAddress => TestClientBuilder.BaseAddress;

    /// <summary>
    /// Runs the startup services through the guarded <see cref="ApplicationLogic.Start"/>, so they
    /// run once whichever attribute the runner reaches first, and puts the routing and handler
    /// filter at the end of the chain - what <c>UseHardened</c> does for the ASP.NET pipeline and
    /// <c>KestrelServerRunner</c> does for Kestrel.
    /// </summary>
    public async Task StartAsync(IServiceProvider provider, CancellationToken cancellationToken) {
        if (_started) {
            return;
        }

        _started = true;
        _provider = provider;

        // Absent where the harness was built by hand rather than run by the runner, which is the
        // constructor taking a provider outright. Then there is one container and this is a host
        // over it, exactly as it was.
        _source = provider.GetService<ITestContainerSource>();

        await ApplicationLogic.Start(provider, null);

        Compose(provider);
    }

    /// <summary>
    /// The container one request runs against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built here rather than reused, so nothing an application singleton accumulated reaches the
    /// next request. What survives is what the test declared: a <c>[Mock]</c>, anything marked
    /// <see cref="SharedAttribute"/>, and the handful of harness services the entry point pins.
    /// </para>
    /// <para>
    /// The startup services run against it on the way out, because <c>ApplicationLogic.Start</c>
    /// keys its guard on the provider rather than on the process. A container that skipped them
    /// would answer every route anonymously with no filter provider installed, which is a container
    /// that looks composed and is not.
    /// </para>
    /// </remarks>
    private async ValueTask<IServiceProvider> ContainerForRequestAsync(bool reuse = false) {
        if (_source is not { } source) {
            return Provider;
        }

        if (reuse) {
            return _reused ??= await Build(source);
        }

        return await Build(source);
    }

    /// <summary>
    /// The one container a caller marked <c>[Shared]</c> reaches, built on its first request.
    /// </summary>
    /// <remarks>
    /// Held here rather than per client, so two clients that both asked to reuse are two callers on
    /// one warm environment - which is the thing they asked to model.
    /// </remarks>
    private IServiceProvider? _reused;

    private async ValueTask<IServiceProvider> Build(ITestContainerSource source) {
        var provider = await source.CreateAsync();

        Compose(provider);

        return provider;
    }

    /// <summary>
    /// Puts routing and the handler filter at the end of one container's chain.
    /// </summary>
    /// <remarks>
    /// Per container rather than once, because <c>MiddlewareService</c> is a singleton holding a
    /// plain list and a fresh container has a fresh empty one. This is what <c>UseHardened</c> does
    /// for the ASP.NET pipeline and <c>KestrelServerRunner</c> does for Kestrel.
    /// </remarks>
    private void Compose(IServiceProvider provider) {
        if (!_appendHandler) {
            return;
        }

        var handler = provider.GetRequiredService<IWebExecutionHandlerService>();

        provider.GetRequiredService<IMiddlewareService>().Use(_ => handler);
    }

    /// <remarks>
    /// The handler outlives one request - a typed client holds it for the test - so it is handed the
    /// way to build a container rather than one container.
    /// </remarks>
    public HttpMessageHandler CreateHandler(TestCredential? credential) =>
        CreateHandler(credential, reuseContainer: false);

    public HttpMessageHandler CreateHandler(TestCredential? credential, bool reuseContainer) =>
        new PipelineHttpMessageHandler(() => ContainerForRequestAsync(reuseContainer), credential);

    public Task<TestWebResponse> SendAsync(
        TestHostRequest request, CancellationToken cancellationToken) =>
        SendAsync(request, cancellationToken, reuseContainer: false);

    public async Task<TestWebResponse> SendAsync(
        TestHostRequest request, CancellationToken cancellationToken, bool reuseContainer) {
        var executionRequest = PipelineRequest.CreateRequest(
            request.Method, request.PathAndQuery, request.Headers, request.Body, request.Credential);
        var body = new MemoryStream();

        var response = await PipelineRequest.Run(
            await ContainerForRequestAsync(reuseContainer), executionRequest, body, cancellationToken);

        return new TestWebResponse(response);
    }

    public ValueTask DisposeAsync() => default;

    private IServiceProvider Provider =>
        _provider ?? throw new InvalidOperationException("The pipeline host has not been started, so it has no container to run a request through.");
}

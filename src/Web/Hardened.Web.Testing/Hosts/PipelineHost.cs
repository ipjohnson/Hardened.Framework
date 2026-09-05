using DependencyModules.Testing.Attributes.Interfaces;
using DependencyModules.Testing.Impl;
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

    /// <summary>A host over a container that is already built and composed, for a harness built by hand.</summary>
    public PipelineHost(IServiceProvider provider) {
        _provider = provider;
        _appendHandler = false;
    }

    internal PipelineHost(bool appendHandler) {
        _appendHandler = appendHandler;
    }

    public bool IsTerminal => true;

    public Uri BaseAddress => TestClientBuilder.BaseAddress;

    /// <summary>
    /// Runs the startup services through the guarded <see cref="ApplicationLogic.Start"/>, so they
    /// run once whichever attribute the runner reaches first, and puts the routing and handler
    /// filter at the end of the chain - what <c>UseHardened</c> does for the ASP.NET pipeline and
    /// <c>KestrelServerRunner</c> does for Kestrel.
    /// </summary>
    public async Task StartAsync(IServiceProvider provider, CancellationToken cancellationToken) {
        _provider = provider;

        await ApplicationLogic.Start(provider, null);

        if (_appendHandler) {
            var handler = provider.GetRequiredService<IWebExecutionHandlerService>();

            provider.GetRequiredService<IMiddlewareService>().Use(_ => handler);
        }
    }

    public HttpMessageHandler CreateHandler(TestCredential? credential) =>
        new PipelineHttpMessageHandler(Provider, credential);

    public async Task<TestWebResponse> SendAsync(TestHostRequest request, CancellationToken cancellationToken) {
        var executionRequest = PipelineRequest.CreateRequest(
            request.Method, request.PathAndQuery, request.Headers, request.Body, request.Credential);
        var body = new MemoryStream();

        var response = await PipelineRequest.Run(Provider, executionRequest, body, cancellationToken);

        return new TestWebResponse(response);
    }

    public ValueTask DisposeAsync() => default;

    private IServiceProvider Provider =>
        _provider ?? throw new InvalidOperationException("The pipeline host has not been started, so it has no container to run a request through.");
}

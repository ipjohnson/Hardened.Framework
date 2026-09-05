using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Metrics;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Requests.Runtime.Logging;
using Hardened.Shared.Runtime.Metrics;
using Hardened.Shared.Testing.Impl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Hardened.Web.Testing.Tests.Conformance;

/// <summary>
/// The least an application root needs for <see cref="PipelineHttpMessageHandler"/> to run a
/// request through it: a chain that does what a test says, a real request logger, and the
/// services the context is built from.
/// </summary>
/// <remarks>
/// A substitute middleware rather than a generated routing table, because what is under test is
/// the transport's translation in and out, not what a handler does inside it - the same footing
/// the Kestrel adapters stand on.
/// </remarks>
internal sealed class SubstitutePipeline {

    public SubstitutePipeline(Func<IExecutionContext, Task>? handler = null) {
        var chain = Substitute.For<IExecutionChain>();
        var middleware = Substitute.For<IMiddlewareService>();

        middleware.GetExecutionChain(Arg.Any<IExecutionContext>()).Returns(callInfo => {
            var context = callInfo.Arg<IExecutionContext>();

            Contexts.Add(context);

            chain.Context.Returns(context);
            chain.Next().Returns(_ => handler?.Invoke(context) ?? Task.CompletedTask);

            return chain;
        });

        var services = new ServiceCollection();

        services.AddSingleton(Substitute.For<IKnownServices>());
        services.AddSingleton(middleware);
        services.AddSingleton<IMetricLoggerProvider>(new NullMetricLoggerProvider());
        services.AddSingleton<Hardened.Requests.Abstract.Logging.IRequestLogger>(
            new RequestLogger(NullLogger<RequestLogger>.Instance));
        services.AddSingleton(new TestCancellationToken(CancellationToken.None));

        Provider = services.BuildServiceProvider();
    }

    public ServiceProvider Provider { get; }

    /// <summary>Every context the chain was asked for, in order.</summary>
    public List<IExecutionContext> Contexts { get; } = new();

    public HttpClient Client(TestCredential? credential = null) =>
        new(new PipelineHttpMessageHandler(Provider, credential)) { BaseAddress = new Uri("http://harness/") };
}

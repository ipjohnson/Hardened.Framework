using Hardened.Azure.Functions.Runtime.Adapters;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.Azure.Functions.ServiceBus;
using Hardened.Azure.Functions.Testing;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Azure.Functions.Runtime.Tests;

/// <summary>
/// The invocation handler's own decisions, apart from the pipeline it hands over to.
/// </summary>
public class FunctionsInvocationHandlerTests {

    /// <summary>
    /// Data no adapter recognises is refused by name. A shim is generated for a family whose
    /// package is referenced, so this is the generator and the modules disagreeing, and a cast
    /// failure somewhere inside an adapter would say nothing about which.
    /// </summary>
    [Fact]
    public async Task DataNoAdapterBindsIsRefusedByName() {
        var services = new ServiceCollection()
            .AddSingleton<IRequestExecutor>(new NeverExecutor())
            .AddSingleton<IMetricLoggerProvider>(new NullMetricsLoggerProvider())
            .AddSingleton<ITriggerAdapter>(new ServiceBusAdapter())
            .BuildServiceProvider();

        var handler = ActivatorUtilities.CreateInstance<FunctionsInvocationHandler>(services);

        var context = new TestFunctionContext("Timer_nightly", new Dictionary<string, object?>(), services);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Invoke(new FunctionsTrigger("TIMER", "/nightly", "a timer payload"), context));

        Assert.Contains("TIMER /nightly", refused.Message);
        Assert.Contains(nameof(ServiceBusAdapter), refused.Message);
    }

    private sealed class NeverExecutor : IRequestExecutor {
        public void Begin(IExecutionContext context) => throw new NotSupportedException();

        public Task RunChain(IExecutionContext context, HostFailurePolicy onFailure) => throw new NotSupportedException();

        public void End(IExecutionContext context) => throw new NotSupportedException();

        public Task Run(IExecutionContext context, HostFailurePolicy onFailure) => throw new NotSupportedException();
    }

    private sealed class NullMetricsLoggerProvider : IMetricLoggerProvider {
        public IMetricLogger CreateLogger(string name) => new NullMetricsLogger();
    }
}

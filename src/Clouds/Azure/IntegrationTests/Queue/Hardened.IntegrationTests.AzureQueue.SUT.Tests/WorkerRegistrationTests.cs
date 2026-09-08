using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.Azure.Functions.ServiceBus;
using Hardened.IntegrationTests.AzureQueue.SUT.Generated;
using Microsoft.Azure.Functions.Worker.Core.FunctionMetadata;
using Microsoft.Azure.Functions.Worker.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.IntegrationTests.AzureQueue.SUT.Tests;

/// <summary>
/// What <c>UseHardened</c> puts in the worker's container, resolved the way the worker resolves it.
///
/// <para>
/// The worker registers a default executor and a default metadata provider of its own and takes
/// whichever registration is last for each. Both of Hardened's have to win, or the host is told
/// about no functions - the default provider reads <c>functions.metadata</c> from disk, which would
/// look right - and invocations run through reflection instead of the generated executor.
/// </para>
/// </summary>
public class WorkerRegistrationTests {

    private static ServiceProvider Worker() {
        var services = new ServiceCollection();

        // The worker's own defaults, exactly as ConfigureFunctionsWorkerDefaults registers them,
        // and then the application on top - which is the order Program.cs produces.
        services.AddFunctionsWorkerDefaults().UseHardened<AzureQueueTestApp>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void TheGeneratedExecutorReplacesTheWorkersDefault() {
        using var worker = Worker();

        Assert.IsType<AzureQueueTestAppAzureFunctionExecutor>(worker.GetRequiredService<IFunctionExecutor>());
    }

    [Fact]
    public void TheGeneratedMetadataProviderReplacesTheWorkersDefault() {
        using var worker = Worker();

        Assert.IsType<AzureQueueTestAppAzureFunctionMetadataProvider>(
            worker.GetRequiredService<IFunctionMetadataProvider>());
    }

    /// <summary>
    /// The shim resolves the invocation handler off the worker's services, so it has to be there,
    /// built with the adapter the queue handler's trigger bound.
    /// </summary>
    [Fact]
    public void TheInvocationHandlerIsInTheWorkerWithTheServiceBusAdapter() {
        using var worker = Worker();

        var handler = worker.GetRequiredService<FunctionsInvocationHandler>();

        Assert.IsType<ServiceBusAdapter>(Assert.Single(handler.Adapters));
    }
}

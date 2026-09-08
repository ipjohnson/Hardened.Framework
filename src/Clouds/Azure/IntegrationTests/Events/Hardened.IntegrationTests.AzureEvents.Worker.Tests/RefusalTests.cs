using DependencyModules.Testing.Attributes;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.Azure.Functions.ServiceBus;
using Hardened.Azure.Functions.Testing;
using Hardened.IntegrationTests.AzureEvents.SUT;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.AzureEvents.SUT.Tests;

/// <summary>
/// Trigger data no adapter in this application claims.
///
/// <para>
/// A shim is generated for every family whose package is referenced, so data of another shape
/// reaching the handler is the generator and the modules disagreeing - which no deployment should
/// see, and which is refused by name rather than guessed at. Only this rung can ask, because only
/// this rung hands the invocation handler the data.
/// </para>
/// </summary>
public class RefusalTests {

    [HardenedTest]
    public async Task DataNoAdapterClaimsFailsTheInvocation(
        IServiceProvider provider, [Mock] ITriggerLog log) {
        var handler = provider.GetRequiredService<FunctionsInvocationHandler>();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Invoke(
                new FunctionsTrigger("STREAM", "/clickstream", Array.Empty<byte>()),
                new TestFunctionContext("Stream_clickstream", new Dictionary<string, object?>(), provider)));

        Assert.Contains(nameof(ServiceBusAdapter), failure.Message);

        log.DidNotReceive().Record(Arg.Any<string>());
    }
}

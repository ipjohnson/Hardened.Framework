using System.Collections.Immutable;
using Hardened.Azure.Functions.Testing;
using Hardened.IntegrationTests.AzureEvents.SUT.Generated;
using Microsoft.Azure.Functions.Worker.Core.FunctionMetadata;
using Xunit;

namespace Hardened.IntegrationTests.AzureEvents.SUT.Tests;

/// <summary>
/// The provider the generator wrote and the file the Worker SDK's build task wrote describe the
/// same four functions - a queue, a subscription, a schedule and the one Event Grid function.
/// </summary>
public class MetadataAgreementTests {
    private static Task<ImmutableArray<IFunctionMetadata>> Declared() =>
        new AzureEventsTestAppAzureFunctionMetadataProvider().GetFunctionMetadataAsync(AppContext.BaseDirectory);

    [Fact]
    public async Task TheProviderAndTheBuildTaskAgree() {
        Assert.Empty(await MetadataAgreement.Disagreements(new AzureEventsTestAppAzureFunctionMetadataProvider()));
    }

    /// <summary>
    /// Four triggers, four functions: one per source for the entity-bound families and one for
    /// every event handler.
    /// </summary>
    [Fact]
    public async Task EveryTriggerHasItsFunction() {
        var names = (await Declared()).Select(function => function.Name ?? "").Order().ToArray();

        Assert.Equal(["Event", "Queue_orders_new", "Timer_nightly_rollup", "Topic_order_events"], names);
    }

    /// <summary>
    /// The subscription the application named reached the topic function's binding, and the
    /// schedule is the app setting named after the timer rather than an expression in the code.
    /// </summary>
    [Fact]
    public async Task TheBindingsCarryWhatTheApplicationSaid() {
        var declared = await Declared();

        var topic = declared.Single(function => function.Name == "Topic_order_events").RawBindings!.Single();
        var timer = declared.Single(function => function.Name == "Timer_nightly_rollup").RawBindings!.Single();

        Assert.Contains("\"subscriptionName\":\"events-function\"", topic);
        Assert.Contains("\"schedule\":\"%Hardened:Timers:nightly-rollup%\"", timer);
    }
}

using Azure.Messaging.ServiceBus;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.Azure.Functions.ServiceBus;
using Hardened.Azure.Functions.Testing;
using Hardened.IntegrationTests.AzureQueue.SUT;
using Hardened.IntegrationTests.AzureQueue.Settlement.SUT;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.AzureQueue.Settlement.SUT.Tests;

/// <summary>
/// The same handlers under a function deployed to settle each message itself.
///
/// <para>
/// The pair of applications is the assertion. <c>AzureQueueTestApp</c> and
/// <c>SettlementTestApp</c> serve identical handlers - the one file, linked - and differ only in
/// whether the deployment said the function settles, so what changes between these tests and the
/// queue fixture's is entirely the failure policy: not the code, not the messages, not the route.
/// </para>
/// <para>
/// <b>Built by hand rather than through [HardenedTest], and this is the case that shows why.</b>
/// What is asserted is which messages the adapter completed and which it abandoned, by id, and
/// that is visible only on the settlement channel the worker binds - so the tests hand the
/// invocation handler the batch and a recording channel directly, the way the SQS fixture hands
/// its invocation handler the envelope.
/// </para>
/// </summary>
public class SettlementTests : IDisposable {
    private readonly ServiceProvider _provider;
    private readonly IOrderStore _store = Substitute.For<IOrderStore>();

    public SettlementTests() {
        _provider = new SettlementTestApp().CreateServiceProvider(
            new EnvironmentImpl(null),
            (_, services) => services.AddSingleton(_store),
            builder => { });
    }

    public void Dispose() => _provider.Dispose();

    private void Refuse(string id) =>
        _store.When(one => one.Place(Arg.Is<Order>(order => order.Id == id)))
            .Do(_ => throw new InvalidOperationException("refused " + id));

    private static ServiceBusReceivedMessage[] Batch(params string[] ids) =>
        ids.Select((id, index) => ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromString($$"""{"id":"{{id}}","quantity":1}"""),
                messageId: "m-" + id,
                contentType: "application/json",
                lockTokenGuid: Guid.NewGuid(),
                deliveryCount: 1,
                sequenceNumber: index + 1))
            .ToArray();

    private async Task Invoke(ServiceBusDelivery delivery) {
        await _provider.GetRequiredService<FunctionsInvocationHandler>().Invoke(
            new FunctionsTrigger("QUEUE", "/orders", delivery),
            new TestFunctionContext("Queue_orders", new Dictionary<string, object?>(), _provider));
    }

    private async Task<RecordingMessageActions> Invoke(params string[] ids) {
        var actions = new RecordingMessageActions();

        await Invoke(new ServiceBusDelivery(Batch(ids), actions));

        return actions;
    }

    /// <summary>
    /// Every message succeeded, so every message is completed. Nothing else would remove them
    /// from the queue: the host's own completion is off for this function.
    /// </summary>
    [Fact]
    public async Task ASuccessfulBatchCompletesEveryMessage() {
        var actions = await Invoke("a-1", "a-2");

        Assert.Equal(["m-a-1", "m-a-2"], actions.Completed);
        Assert.Empty(actions.Abandoned);
    }

    /// <summary>
    /// The invocation succeeds, the one message that failed is abandoned so the queue delivers it
    /// again, and the rest are completed. Without this the whole batch would be redelivered,
    /// including the messages that were handled.
    /// </summary>
    [Fact]
    public async Task OnlyTheFailedMessageIsAbandoned() {
        Refuse("a-2");

        var actions = await Invoke("a-1", "a-2", "a-3");

        Assert.Equal(["m-a-2"], actions.Abandoned);
        Assert.Equal(["m-a-1", "m-a-3"], actions.Completed);
    }

    /// <summary>
    /// Stopping at the first failure would leave the rest unhandled and unsettled, and their
    /// locks would expire into a redelivery of messages that were never attempted.
    /// </summary>
    [Fact]
    public async Task EveryMessageIsStillAttemptedAfterOneFails() {
        Refuse("a-1");

        var actions = await Invoke("a-1", "a-2", "a-3");

        Assert.Equal(["m-a-1"], actions.Abandoned);

        _store.Received(3).Place(Arg.Any<Order>());
    }

    [Fact]
    public async Task SeveralFailuresAreAllAbandoned() {
        Refuse("a-1");
        Refuse("a-3");

        var actions = await Invoke("a-1", "a-2", "a-3");

        Assert.Equal(["m-a-1", "m-a-3"], actions.Abandoned);
        Assert.Equal(["m-a-2"], actions.Completed);
    }

    /// <summary>
    /// Every message failing is still a successful invocation with every message abandoned,
    /// rather than a throw. Failing the invocation as well would count the same messages twice
    /// against the delivery limit.
    /// </summary>
    [Fact]
    public async Task AWhollyFailedBatchAbandonsEveryMessage() {
        Refuse("a-1");
        Refuse("a-2");

        var actions = await Invoke("a-1", "a-2");

        Assert.Equal(["m-a-1", "m-a-2"], actions.Abandoned);
        Assert.Empty(actions.Completed);
    }

    /// <summary>
    /// Without the settlement channel there is nothing to settle through, so the function is back
    /// to the default: a failed message fails the invocation and the host abandons the batch.
    /// </summary>
    [Fact]
    public async Task WithoutASettlementChannelAFailureFailsTheInvocation() {
        Refuse("a-2");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Invoke(new ServiceBusDelivery(Batch("a-1", "a-2"))));
    }
}

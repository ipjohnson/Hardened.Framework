using Azure.Messaging.ServiceBus;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Azure.Functions.ServiceBus;
using Hardened.Azure.Functions.Testing;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Azure.Functions.Runtime.Tests;

/// <summary>
/// What a Service Bus message becomes on its way to a handler.
/// </summary>
public class ServiceBusRequestTests {

    private static ServiceBusRequest Batch(params ServiceBusReceivedMessage[] messages) {
        var context = new TestFunctionContext(
            "Queue_orders", new Dictionary<string, object?>(), new ServiceCollection().BuildServiceProvider());

        return (ServiceBusRequest)new ServiceBusAdapter().CreateRequest(
            new FunctionsTrigger("QUEUE", "/orders", messages), context);
    }

    private static ServiceBusReceivedMessage Message(
        string body = "{}",
        string messageId = "m-1",
        int deliveryCount = 1,
        IDictionary<string, object>? properties = null,
        string? contentType = null) =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(body),
            messageId: messageId,
            deliveryCount: deliveryCount,
            properties: properties,
            contentType: contentType);

    [Fact]
    public void TheBatchRoutesUnderTheQueueSchemeAndTheShimsPath() {
        var batch = Batch(Message());

        Assert.Equal("QUEUE", batch.Method);
        Assert.Equal("/orders", batch.Path);
    }

    [Fact]
    public void OneForkPerMessageInDeliveryOrder() {
        var batch = Batch(Message(messageId: "m-1"), Message(messageId: "m-2"));

        Assert.Equal(2, batch.Count);
        Assert.Equal("m-1", batch.ForItem(0).Headers[ServiceBusRequest.MessageIdHeader].ToString());
        Assert.Equal("m-2", batch.ForItem(1).Headers[ServiceBusRequest.MessageIdHeader].ToString());
    }

    /// <summary>
    /// The delivery count is how a handler tells a retry from a first attempt, which is the fact
    /// the abandon-the-batch policy makes worth carrying.
    /// </summary>
    [Fact]
    public void TheDeliveryCountIsAHeader() {
        var fork = Batch(Message(deliveryCount: 3)).ForItem(0);

        Assert.Equal("3", fork.Headers[ServiceBusRequest.DeliveryCountHeader].ToString());
    }

    [Fact]
    public void ApplicationPropertiesBecomeHeadersUnderTheirOwnNames() {
        var fork = Batch(Message(properties: new Dictionary<string, object> {
            ["trace"] = "abc-123",
            ["attempt"] = 7,
            ["raw"] = new byte[] { 1, 2 }
        })).ForItem(0);

        Assert.Equal("abc-123", fork.Headers["trace"].ToString());
        Assert.Equal("7", fork.Headers["attempt"].ToString());

        // A binary property is not a header, and rendering it as base64 under the same name would
        // make a handler unable to tell the two apart.
        Assert.False(fork.Headers.ContainsKey("raw"));
    }

    /// <summary>
    /// A property named like the message id cannot overwrite it: the message's own facts live
    /// under prefixed names.
    /// </summary>
    [Fact]
    public void ThePrefixedHeadersWinOverAPropertyOfTheSameSpelling() {
        var fork = Batch(Message(messageId: "real", properties: new Dictionary<string, object> {
            [ServiceBusRequest.MessageIdHeader] = "forged"
        })).ForItem(0);

        Assert.Equal("real", fork.Headers[ServiceBusRequest.MessageIdHeader].ToString());
    }

    [Fact]
    public void TheContentTypeTravelsWhenThePublisherSetOne() {
        var fork = Batch(Message(contentType: "application/json")).ForItem(0);

        Assert.Equal("application/json", fork.ContentType);
    }

    [Fact]
    public void EachForkCarriesItsOwnBody() {
        var batch = Batch(Message(body: "{\"id\":\"a\"}"), Message(body: "{\"id\":\"b\"}"));

        Assert.Equal("{\"id\":\"a\"}", new StreamReader(batch.ForItem(0).Body).ReadToEnd());
        Assert.Equal("{\"id\":\"b\"}", new StreamReader(batch.ForItem(1).Body).ReadToEnd());
    }

    [Fact]
    public void AnEmptyMessageHasAnEmptyBodyRatherThanNone() {
        var fork = Batch(Message(body: "")).ForItem(0);

        Assert.Equal(0, fork.Body.Length);
    }

    /// <summary>
    /// Off until Phase 2's settlement lands: the host settles the whole batch on the invocation's
    /// outcome, so the filter has to fail the invocation to get a message redelivered.
    /// </summary>
    [Fact]
    public void ItemFailuresAreNotReportedYet() {
        var batch = Batch(Message());

        Assert.False(batch.ReportsItemFailures);
        Assert.Equal(BatchFailureMode.PerItem, batch.FailureMode);
    }
}

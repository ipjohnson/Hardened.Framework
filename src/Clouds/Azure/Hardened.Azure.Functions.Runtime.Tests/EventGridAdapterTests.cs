using Hardened.Azure.Functions.EventGrid;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Azure.Functions.Testing;
using Hardened.CloudEvents;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Azure.Functions.Runtime.Tests;

public class EventGridAdapterTests {
    private static readonly EventGridAdapter Adapter = new();

    private const string Event = """
        {"specversion":"1.0","id":"7bf73129","source":"com.acme.orders","type":"OrderPlaced",
         "subject":"orders/e-1","time":"2026-09-07T12:00:00Z","datacontenttype":"application/json",
         "data":{"id":"e-1","quantity":5}}
        """;

    private static TestFunctionContext Context() =>
        new("Event", new Dictionary<string, object?>(), new ServiceCollection().BuildServiceProvider());

    [Fact]
    public void HandlesAStringOnlyUnderTheEventScheme() {
        Assert.True(Adapter.Handles(new FunctionsTrigger(CloudEventRoutes.EventScheme, "", Event)));
        Assert.False(Adapter.Handles(new FunctionsTrigger("TIMER", "/nightly", "{}")));
    }

    /// <summary>
    /// The shim carries no route; the event does. Source and type become the path, the way the
    /// EventBridge adapter routes, and the data is the body.
    /// </summary>
    [Fact]
    public void TheRouteComesFromTheEventAndTheBodyIsItsData() {
        var request = Adapter.CreateRequest(
            new FunctionsTrigger(CloudEventRoutes.EventScheme, "", Event), Context());

        Assert.Equal("EVENT", request.Method);
        Assert.Equal("/com.acme.orders/OrderPlaced", request.Path);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("""{"id":"e-1","quantity":5}""", new StreamReader(request.Body).ReadToEnd());
    }

    /// <summary>
    /// Under the names the lines share, so a handler reading the event id reads the same header
    /// behind Event Grid and behind Eventarc.
    /// </summary>
    [Fact]
    public void TheEventsAttributesAreHeaders() {
        var request = Adapter.CreateRequest(
            new FunctionsTrigger(CloudEventRoutes.EventScheme, "", Event), Context());

        Assert.Equal("1.0", request.Headers[CloudEventHeaders.SpecVersion].ToString());
        Assert.Equal("7bf73129", request.Headers[CloudEventHeaders.Id].ToString());
        Assert.Equal("com.acme.orders", request.Headers[CloudEventHeaders.Source].ToString());
        Assert.Equal("OrderPlaced", request.Headers[CloudEventHeaders.Type].ToString());
        Assert.Equal("orders/e-1", request.Headers[CloudEventHeaders.Subject].ToString());
        Assert.Equal("2026-09-07T12:00:00Z", request.Headers[CloudEventHeaders.Time].ToString());
    }

    [Fact]
    public void AnEventWithoutDataHasNoBody() {
        var request = Adapter.CreateRequest(
            new FunctionsTrigger(
                CloudEventRoutes.EventScheme, "",
                """{"specversion":"1.0","id":"1","source":"com.acme.orders","type":"OrderCancelled"}"""),
            Context());

        Assert.Same(Stream.Null, request.Body);
        Assert.Equal("/com.acme.orders/OrderCancelled", request.Path);
    }
}

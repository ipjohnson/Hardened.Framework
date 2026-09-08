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

    [Fact]
    public void TheEventsAttributesAreHeaders() {
        var request = Adapter.CreateRequest(
            new FunctionsTrigger(CloudEventRoutes.EventScheme, "", Event), Context());

        Assert.Equal("7bf73129", request.Headers[EventGridAdapter.IdHeader].ToString());
        Assert.Equal("com.acme.orders", request.Headers[EventGridAdapter.SourceHeader].ToString());
        Assert.Equal("OrderPlaced", request.Headers[EventGridAdapter.TypeHeader].ToString());
        Assert.Equal("orders/e-1", request.Headers[EventGridAdapter.SubjectHeader].ToString());
        Assert.Equal("2026-09-07T12:00:00Z", request.Headers[EventGridAdapter.TimeHeader].ToString());
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

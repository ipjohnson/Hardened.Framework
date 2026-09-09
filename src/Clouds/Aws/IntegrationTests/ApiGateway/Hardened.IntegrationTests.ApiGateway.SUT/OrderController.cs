using System.Runtime.CompilerServices;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.ApiGateway.SUT;

public class Order {
    public string Id { get; set; } = "";

    public int Quantity { get; set; }
}

/// <summary>
/// Ordinary web handlers, with nothing on them that knows where they are hosted.
/// </summary>
public class OrderController {
    [Get("/orders/{id}")]
    public Order Get(string id) => new() { Id = id, Quantity = 7 };

    [Post("/orders")]
    public Order Place(Order order) => order;

    [Delete("/orders/{id}")]
    public void Remove(string id) { }

    /// <summary>
    /// An event stream, so this application has something for the routing generator to put in its
    /// <c>IServerSentEventManifest</c> and for the host to warn about when it is deployed buffered.
    /// </summary>
    /// <remarks>
    /// A literal segment beside <c>/orders/{id}</c> on purpose. Nothing else in the repository
    /// routes a literal and a wildcard at the same depth, and an event stream at
    /// <c>/orders/live</c> is the shape anyone would reach for.
    /// </remarks>
    [Get("/orders/live")]
    [ServerSentEvents]
    public async IAsyncEnumerable<Order> Live([EnumeratorCancellation] CancellationToken cancellationToken) {
        yield return new Order { Id = "live-1", Quantity = 1 };

        await Task.Yield();
    }

    /// <summary>
    /// The same thing under a constrained token, so the manifest has a route whose template carries
    /// routing syntax. The host prints these to an operator, and a constraint is not part of the
    /// route anyone wrote down - a pattern a contract declared is named after a hash of itself.
    /// </summary>
    [Get("/orders/{id:int}/live")]
    [ServerSentEvents]
    public async IAsyncEnumerable<Order> LiveForOrder(
        int id, [EnumeratorCancellation] CancellationToken cancellationToken) {
        yield return new Order { Id = id.ToString(), Quantity = 1 };

        await Task.Yield();
    }
}

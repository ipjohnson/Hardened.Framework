#if (invoke)
using Hardened.Requests.Abstract.Attributes;
#endif
#if (delivery)
using Hardened.Functions.Runtime.Attributes;
#endif

namespace Hardened1;

/// <summary>
/// The function. A plain class - no base type, no interface, no registration.
/// </summary>
/// <remarks>
/// The attribute is the whole declaration. It names a source and nothing else: no cloud, no
/// adapter, no module. The generator finds it, binds the payload to the parameter, resolves the
/// dependencies from the container, and registers whatever serves that trigger on the host you
/// chose.
/// </remarks>
public class OrderHandler(OrderLog log) {

#if (invoke)
    /// <summary>
    /// A direct invocation, which is the one shape that answers: the caller waits on the return
    /// value and gets it back serialised.
    /// </summary>
    /// <remarks>
    /// Returns the value rather than a Task of it. Either compiles and both are invoked the same
    /// way; the generated test façade reads the declared return type without unwrapping a Task, so
    /// a synchronous return is what lets a test assert on the answer.
    /// </remarks>
    [HardenedFunction]
    public OrderAccepted Process(Order order) {
        log.Record(order);

        return new OrderAccepted(order.Id, log.Orders.Count);
    }
#endif
#if (queue)
    /// <summary>
    /// One call per message. The queue delivers a batch and the fan-out is the framework's, so
    /// this method sees a single order and never the batch it arrived in.
    /// </summary>
    /// <remarks>
    /// Returning normally handles the message. Throwing fails the invocation, which is what
    /// returns it to the queue - there is no other way to say "not handled".
    /// </remarks>
    [Queue("orders")]
    public void OnOrder(Order order) => log.Record(order);
#endif
#if (topic)
    /// <summary>
    /// One call per notification. A topic fans out to every subscriber, so this is one of them.
    /// </summary>
    [Topic("orders")]
    public void OnOrder(Order order) => log.Record(order);
#endif
#if (timer)
    /// <summary>
    /// Called on the schedule the deployment wires to this name. No payload: a schedule carries
    /// nothing but the fact that it fired.
    /// </summary>
    [Timer("nightly")]
    public void OnNightly() => log.Sweep();
#endif
#if (change)
    /// <summary>
    /// One call per row that changed. The row arrives as an ordinary object - the store's own
    /// wire form is unwrapped before it reaches here.
    /// </summary>
    /// <remarks>
    /// A change feed is ordered and replayable, so a failure rewinds rather than singling one row
    /// out: everything after the failure is redelivered. Make this idempotent.
    /// </remarks>
    [Change("orders")]
    public void OnOrderChanged(Order order) => log.Record(order);
#endif
#if (stream)
    /// <summary>
    /// One call per record. A stream carries the publisher's own bytes and says nothing about
    /// what is in them, so the parameter type is the contract between you and whoever writes.
    /// </summary>
    /// <remarks>
    /// Ordered per partition key and replayable, so a failure rewinds. Make this idempotent.
    /// </remarks>
    [Stream("orders")]
    public void OnOrder(Order order) => log.Record(order);
#endif
#if (blob)
    /// <summary>
    /// One call per object that changed.
    /// </summary>
    /// <remarks>
    /// <b>The notification is the message and the object is not in it.</b> A store sends the
    /// bucket, the key and the size; fetching the object is a call you make yourself. That is the
    /// transport's design rather than the framework's.
    /// </remarks>
    [Blob("uploads")]
    public void OnUpload(Upload upload) => log.Record(upload);
#endif
}
#if (invoke)

/// <summary>What the caller gets back, serialised by the host.</summary>
public record OrderAccepted(string Id, int Received);
#endif

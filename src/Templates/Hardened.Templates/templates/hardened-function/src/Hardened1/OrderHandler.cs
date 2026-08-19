using Hardened.Requests.Abstract.Attributes;

namespace Hardened1;

/// <summary>
/// The function. A plain class - no base type, no interface, no registration.
/// </summary>
/// <remarks>
/// [HardenedFunction] is what the generator finds. It writes an entry point bound to this
/// method's exact signature: the payload is deserialised into the parameter, the dependencies
/// come from the container, and the return value is serialised back.
/// </remarks>
public class OrderHandler(OrderLog log) {

#if (sqs)
    /// <summary>
    /// Called once per record in the batch. Returning normally marks that record handled;
    /// throwing reports it as a batch item failure, and only those are redelivered.
    /// </summary>
    [HardenedFunction]
    public Task Process(Order order) {
        log.Record(order);

        return Task.CompletedTask;
    }
#endif
#if (invoke)
    [HardenedFunction]
    public Task<OrderAccepted> Process(Order order) {
        log.Record(order);

        return Task.FromResult(new OrderAccepted(order.Id, log.Orders.Count));
    }
#endif
}
#if (invoke)

/// <summary>What the caller gets back. Serialised by the generated entry point.</summary>
public record OrderAccepted(string Id, int Received);
#endif

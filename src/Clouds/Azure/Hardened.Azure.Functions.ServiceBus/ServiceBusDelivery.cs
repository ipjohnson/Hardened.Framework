using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;

namespace Hardened.Azure.Functions.ServiceBus;

/// <summary>
/// What a Service Bus shim hands the invocation handler: the batch, and the actions that settle it.
/// </summary>
/// <remarks>
/// <para>
/// The generated shim binds two things from the worker, the messages and a
/// <see cref="ServiceBusMessageActions"/>, and the invocation handler carries one object per
/// invocation. This is that object. The actions are bound whether or not the module reports item
/// failures - binding them costs a client the extension already holds and no call - so the shim's
/// shape does not change with a deployment setting, and only what the adapter does with them does.
/// </para>
/// <para>
/// <see cref="Actions"/> is null where nothing bound them: the envelope tier's delivery, which has
/// no host to settle with, and a test that builds a delivery by hand.
/// </para>
/// </remarks>
public sealed class ServiceBusDelivery {
    public ServiceBusDelivery(ServiceBusReceivedMessage[] messages, ServiceBusMessageActions? actions = null) {
        Messages = messages;
        Actions = actions;
    }

    /// <summary>The batch, in the order the queue or subscription delivered it.</summary>
    public ServiceBusReceivedMessage[] Messages { get; }

    /// <summary>The settlement actions for this invocation's messages, when the worker bound them.</summary>
    public ServiceBusMessageActions? Actions { get; }
}

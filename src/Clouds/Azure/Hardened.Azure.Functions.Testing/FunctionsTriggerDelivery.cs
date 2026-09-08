using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.Functions.Testing;

namespace Hardened.Azure.Functions.Testing;

/// <summary>
/// Turns a message and a route into what the worker would have bound for the function, and invokes.
/// </summary>
/// <remarks>
/// <para>
/// The higher-fidelity half of <see cref="ITriggerDelivery"/>. A test says
/// <c>queues.Orders(order)</c> and what runs is the real trigger data through the real invocation
/// handler: the adapter recognises its type, the batch filter forks it, the binder binds each
/// message's body. The neutral delivery covers everything from routing inwards; this adds the parts
/// only a provider knows - the message type, its metadata and the body encoding.
/// </para>
/// <para>
/// The messages are built through <see cref="ServiceBusModelFactory"/>, which is the SDK's own
/// door for a received message a test did not receive: it produces the type the extension's
/// converter produces, with the delivery count, sequence number and lock token a real one carries.
/// What this delivery cannot exercise is the converter itself, which turns the host's AMQP bytes
/// into that type; that is the container tier's job.
/// </para>
/// </remarks>
public sealed class FunctionsTriggerDelivery : ITriggerDelivery {
    private readonly FunctionsInvocationHandler _handler;
    private readonly IServiceProvider _provider;

    public FunctionsTriggerDelivery(FunctionsInvocationHandler handler, IServiceProvider provider) {
        _handler = handler;
        _provider = provider;
    }

    /// <summary>
    /// camelCase, which is what a publisher sends and what the handler's binder is set up to read.
    /// </summary>
    private static readonly JsonSerializerOptions Wire =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task Deliver(IReadOnlyList<object> messages, string scheme, string path) {
        var name = path.TrimStart('/');

        object data;
        IReadOnlyDictionary<string, object?> bindingData;

        switch (scheme) {
            case "QUEUE":
                var received = ServiceBus(name, messages);

                data = received;
                bindingData = ServiceBusBindingData(received);

                break;

            default:
                throw new NotSupportedException(
                    $"No worker trigger data is built for the {scheme} scheme yet. Queues have one; " +
                    "the other families arrive with their adapter packages.");
        }

        var context = new TestFunctionContext(
            Generated.FunctionName(scheme, name), bindingData, _provider);

        await _handler.Invoke(new FunctionsTrigger(scheme, path, data), context);
    }

    /// <summary>
    /// Not bound on Azure. There is no direct invocation of a function; the nearest thing is an
    /// HTTP trigger with a function key, which is a different family.
    /// </summary>
    public Task<object?> Call(object message, string scheme, string path, Type? responseType) =>
        throw new NotSupportedException(
            "Azure Functions has no direct invoke, so [HardenedFunction] is not bound on this " +
            "provider and nothing delivers to it here. The pipeline delivery in " +
            "Hardened.Functions.Testing still reaches the handler.");

    /// <summary>
    /// The batch as the extension's converter would produce it: one received message per test
    /// message, on its first delivery, with the body the publisher would have sent.
    /// </summary>
    private static ServiceBusReceivedMessage[] ServiceBus(string queue, IReadOnlyList<object> messages) {
        var received = new ServiceBusReceivedMessage[messages.Count];

        for (var index = 0; index < messages.Count; index++) {
            received[index] = ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(messages[index], Wire)),
                messageId: queue + "-" + index,
                contentType: "application/json",
                lockTokenGuid: Guid.NewGuid(),
                deliveryCount: 1,
                sequenceNumber: index + 1,
                enqueuedTime: DateTimeOffset.UtcNow);
        }

        return received;
    }

    /// <summary>
    /// The trigger metadata the host sends beside a batch: parallel arrays, one entry per message,
    /// each array serialized to a JSON string. Nothing in the adapter reads them today - the
    /// message carries the same facts - so what is here is the shape, for the day something does.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ServiceBusBindingData(
        IReadOnlyList<ServiceBusReceivedMessage> messages) =>
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
            ["MessageIdArray"] = JsonSerializer.Serialize(messages.Select(message => message.MessageId)),
            ["DeliveryCountArray"] = JsonSerializer.Serialize(messages.Select(message => message.DeliveryCount)),
            ["SequenceNumberArray"] = JsonSerializer.Serialize(messages.Select(message => message.SequenceNumber)),
            ["EnqueuedTimeUtcArray"] = JsonSerializer.Serialize(messages.Select(message => message.EnqueuedTime)),
            ["ContentTypeArray"] = JsonSerializer.Serialize(messages.Select(message => message.ContentType))
        };

    /// <summary>
    /// What the generator names things, spelled here for the test context's function name.
    /// </summary>
    /// <remarks>
    /// The same rule as <c>AzureFunctionsGenerator.FunctionName</c>: the trigger's kind, an
    /// underscore, and the source with everything outside letters and digits replaced. A copy
    /// rather than a reference, because a generator assembly is not something a runtime package
    /// can reference.
    /// </remarks>
    private static class Generated {
        public static string FunctionName(string scheme, string source) {
            var kind = scheme.Length > 1
                ? scheme.Substring(0, 1) + scheme.Substring(1).ToLowerInvariant()
                : scheme;

            var name = new System.Text.StringBuilder(kind).Append('_');

            foreach (var character in source) {
                name.Append(char.IsLetterOrDigit(character) ? character : '_');
            }

            return name.ToString();
        }
    }
}

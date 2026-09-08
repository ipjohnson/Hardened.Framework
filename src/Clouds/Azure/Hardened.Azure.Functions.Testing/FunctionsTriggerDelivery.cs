using System.Text;
using System.Text.Json;
using Azure.Messaging.EventHubs;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Azure.Functions.Runtime.Hosting;
using Hardened.Azure.Functions.ServiceBus;
using Hardened.Functions.Testing;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;

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
/// only a provider knows - the type the extension's converter produces, its metadata and the body
/// encoding.
/// </para>
/// <para>
/// The data is built through each SDK's own model factory where one exists -
/// <see cref="ServiceBusModelFactory"/>, <see cref="EventHubsModelFactory"/> - which is the SDK's
/// door for a received message a test did not receive. What this delivery cannot exercise is the
/// converter itself, which turns the host's bytes into that type; that is the container tier's job.
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

    private static readonly IReadOnlyDictionary<string, object?> NoBindingData =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public async Task Deliver(IReadOnlyList<object> messages, string scheme, string path) {
        var name = path.TrimStart('/');

        switch (scheme) {
            case "QUEUE":
            case "TOPIC": {
                var received = ServiceBus(name, messages);

                await Invoke(
                    scheme, path, Generated.FunctionName(scheme, name),
                    new ServiceBusDelivery(received, _provider.GetService<ServiceBusMessageActions>()),
                    ServiceBusBindingData(received));

                break;
            }

            case "TIMER":
                // No batch: the host fires a timer function once per occurrence, so a façade call
                // is one invocation and the messages, if any, are how many.
                for (var occurrence = 0; occurrence < Math.Max(1, messages.Count); occurrence++) {
                    await Invoke(scheme, path, Generated.FunctionName(scheme, name), Timer(), NoBindingData);
                }

                break;

            case "STREAM": {
                var events = EventHubs(name, messages);

                await Invoke(
                    scheme, path, Generated.FunctionName(scheme, name), events, EventHubsBindingData(events));

                break;
            }

            case "CHANGE":
                await Invoke(scheme, path, Generated.FunctionName(scheme, name), ChangeFeed(messages), NoBindingData);

                break;

            case "BLOB":
                // One blob per invocation, which is how the trigger fires.
                for (var index = 0; index < messages.Count; index++) {
                    var (blob, bindingData) = Blob(name, messages[index], index);

                    await Invoke(scheme, path, Generated.FunctionName(scheme, name), blob, bindingData);
                }

                break;

            case "EVENT":
                // Addressed by source and type, which the path carries as /{source}/{type}, the
                // shape the adapter routes under. One CloudEvent per invocation.
                foreach (var message in messages) {
                    await Invoke(scheme, "", "Event", CloudEvent(name, message), NoBindingData);
                }

                break;

            default:
                throw new NotSupportedException(
                    $"No worker trigger data is built for the {scheme} scheme. Queues, topics, " +
                    "timers, streams, changes, blobs and events have one.");
        }
    }

    private async Task Invoke(
        string scheme, string path, string function, object data, IReadOnlyDictionary<string, object?> bindingData) {
        var context = new TestFunctionContext(function, bindingData, _provider);

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
    private static ServiceBusReceivedMessage[] ServiceBus(string source, IReadOnlyList<object> messages) {
        var received = new ServiceBusReceivedMessage[messages.Count];

        for (var index = 0; index < messages.Count; index++) {
            received[index] = ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(messages[index], Wire)),
                messageId: source + "-" + index,
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
    /// The timer's state as the host sends it: the schedule, when it last ran and will next, and
    /// whether this occurrence is late.
    /// </summary>
    private static string Timer() {
        var now = DateTimeOffset.UtcNow;

        return JsonSerializer.Serialize(new Dictionary<string, object?> {
            ["Schedule"] = new Dictionary<string, object?> { ["AdjustForDST"] = true },
            ["ScheduleStatus"] = new Dictionary<string, object?> {
                ["Last"] = now.AddHours(-1),
                ["LastUpdated"] = now.AddHours(-1),
                ["Next"] = now.AddHours(1)
            },
            ["IsPastDue"] = false
        });
    }

    /// <summary>
    /// The batch as the extension's converter would produce it: one event per test message, in
    /// order, with ascending sequence numbers and offsets, which is what a partition guarantees.
    /// </summary>
    private static EventData[] EventHubs(string hub, IReadOnlyList<object> messages) {
        var events = new EventData[messages.Count];

        for (var index = 0; index < messages.Count; index++) {
            var eventData = EventHubsModelFactory.EventData(
                eventBody: BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(messages[index], Wire)),
                partitionKey: hub,
                sequenceNumber: index + 1,
                offset: (index + 1) * 64,
                enqueuedTime: DateTimeOffset.UtcNow);

            eventData.ContentType = "application/json";
            eventData.MessageId = hub + "-" + index;

            events[index] = eventData;
        }

        return events;
    }

    private static IReadOnlyDictionary<string, object?> EventHubsBindingData(IReadOnlyList<EventData> events) =>
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
            ["PartitionContext"] = JsonSerializer.Serialize(new Dictionary<string, object?> {
                ["PartitionId"] = "0"
            }),
            ["SequenceNumberArray"] = JsonSerializer.Serialize(events.Select(one => one.SequenceNumber)),
            ["OffsetArray"] = JsonSerializer.Serialize(events.Select(one => one.Offset)),
            ["EnqueuedTimeUtcArray"] = JsonSerializer.Serialize(events.Select(one => one.EnqueuedTime)),
            ["PartitionKeyArray"] = JsonSerializer.Serialize(events.Select(one => one.PartitionKey))
        };

    /// <summary>
    /// The batch as the host sends it: one JSON array of the documents that changed, each with the
    /// system properties Cosmos stamps on a document.
    /// </summary>
    /// <remarks>
    /// The message is written out and the properties added beside its own, so what the adapter
    /// splits is a document with <c>_lsn</c>, <c>_ts</c> and <c>_etag</c> on it rather than the
    /// message as the test wrote it. The ids ascend, which is what a partition's feed guarantees.
    /// </remarks>
    private static string ChangeFeed(IReadOnlyList<object> messages) {
        var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer)) {
            writer.WriteStartArray();

            for (var index = 0; index < messages.Count; index++) {
                using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(messages[index], Wire));

                writer.WriteStartObject();

                var hasId = false;

                if (document.RootElement.ValueKind == JsonValueKind.Object) {
                    foreach (var property in document.RootElement.EnumerateObject()) {
                        hasId |= property.Name == "id";
                        property.WriteTo(writer);
                    }
                }

                if (!hasId) {
                    writer.WriteString("id", "document-" + index);
                }

                writer.WriteString("_rid", "rid-" + index);
                writer.WriteString("_etag", "\"" + index.ToString("D8") + "\"");
                writer.WriteNumber("_ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                writer.WriteNumber("_lsn", 100 + index);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// The blob as the extension's converter binds it - a client for the blob's URI, which costs
    /// no request - and the properties the host sends beside it.
    /// </summary>
    /// <remarks>
    /// <b>The message is the notification, not the blob.</b> Storage sends a name and a size and
    /// nothing else, so a test writing <c>blobs.Uploads(new Upload { Name = "a.txt", Size = 12 })</c>
    /// is describing what Storage would say about a blob rather than the blob itself - which is
    /// what a blob handler binds. The name and size are read off the message under those
    /// properties; anything else on it is ignored, because a notification has nowhere to carry it.
    /// </remarks>
    private static (BlobClient Blob, IReadOnlyDictionary<string, object?> BindingData) Blob(
        string container, object message, int index) {
        using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(message, Wire));

        var name = document.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() ?? ""
            : $"blob-{index}";

        var size = document.RootElement.TryGetProperty("size", out var z) && z.ValueKind == JsonValueKind.Number
            ? z.GetInt64()
            : 0;

        var uri = new Uri(
            "https://devstoreaccount1.blob.core.windows.net/" + container + "/" +
            string.Join("/", name.Split('/').Select(Uri.EscapeDataString)));

        var bindingData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
            ["BlobTrigger"] = container + "/" + name,
            ["Uri"] = uri.ToString(),
            ["Properties"] = JsonSerializer.Serialize(new Dictionary<string, object?> {
                ["Length"] = size,
                ["ContentType"] = "application/octet-stream",
                ["ETag"] = "\"0x" + index.ToString("X16") + "\"",
                ["LastModified"] = DateTimeOffset.UtcNow
            }),
            ["Metadata"] = "{}"
        };

        return (new BlobClient(uri), bindingData);
    }

    /// <summary>
    /// The event as the host sends a function subscribed in the CloudEvents schema: one structured
    /// CloudEvent, with the message as its data.
    /// </summary>
    private static string CloudEvent(string route, object message) {
        var separator = route.LastIndexOf('/');

        var source = separator < 0 ? route : route.Substring(0, separator);
        var type = separator < 0 ? "" : route.Substring(separator + 1);

        var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer)) {
            writer.WriteStartObject();
            writer.WriteString("specversion", "1.0");
            writer.WriteString("id", Guid.NewGuid().ToString());
            writer.WriteString("source", source);
            writer.WriteString("type", type);
            writer.WriteString("time", DateTimeOffset.UtcNow);
            writer.WriteString("datacontenttype", "application/json");
            writer.WritePropertyName("data");

            using (var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(message, Wire))) {
                document.RootElement.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

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

            var name = new StringBuilder(kind).Append('_');

            foreach (var character in source) {
                name.Append(char.IsLetterOrDigit(character) ? character : '_');
            }

            return name.ToString();
        }
    }
}

using System.Globalization;
using Azure.Messaging.ServiceBus;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Primitives;

namespace Hardened.Azure.Functions.ServiceBus;

/// <summary>
/// One Service Bus delivery: a queue, and the messages it carried.
/// </summary>
/// <remarks>
/// <para>
/// <b>One request for the batch, not one per message.</b> The function is bound to a single queue,
/// so every message in an invocation routes to the same handler - the routing decision is made
/// once and the fan-out is a pipeline concern. <see cref="Messages"/> is how the batch filter
/// reaches them; it forks this request per message through <see cref="ForItem"/>.
/// </para>
/// <para>
/// <see cref="ReportsItemFailures"/> follows the module. Off, the host settles the whole batch on
/// the invocation's outcome, so a failed message has to fail the invocation to be redelivered. On,
/// the filter records the failed messages and the adapter settles each one through
/// <see cref="Actions"/> once the chain has finished.
/// </para>
/// </remarks>
public class ServiceBusRequest : FunctionsPayloadRequest, IBatchRequest {
    private readonly List<int> _failed = [];

    public ServiceBusRequest(
        string scheme,
        string path,
        Stream body,
        IDictionary<string, StringValues> headers,
        IReadOnlyList<ServiceBusReceivedMessage> messages,
        bool reportsItemFailures = false,
        Microsoft.Azure.Functions.Worker.ServiceBusMessageActions? actions = null)
        : base(scheme, path, body, headers) {
        Messages = messages;
        ReportsItemFailures = reportsItemFailures && actions != null;
        Actions = actions;
    }

    /// <summary>The messages, in the order the queue delivered them.</summary>
    public IReadOnlyList<ServiceBusReceivedMessage> Messages { get; }

    /// <summary>
    /// What settles this batch's messages, when the worker bound them. Null under the envelope
    /// tier's delivery, which has no host to settle with - and then
    /// <see cref="ReportsItemFailures"/> is false whatever the module said, because recording a
    /// failure nothing could act on would lose the message.
    /// </summary>
    public Microsoft.Azure.Functions.Worker.ServiceBusMessageActions? Actions { get; }

    /// <summary>The message id, as a header, so a handler can log or deduplicate on it.</summary>
    public const string MessageIdHeader = "x-azure-servicebus-message-id";

    /// <summary>
    /// How many times the queue has delivered this message, so a handler can tell a retry from a
    /// first attempt.
    /// </summary>
    public const string DeliveryCountHeader = "x-azure-servicebus-delivery-count";

    public int Count => Messages.Count;

    public IExecutionRequest ForItem(int index) => ForMessage(Messages[index]);

    public bool ReportsItemFailures { get; }

    /// <summary>
    /// Per item, because a queue has no order to keep. Only consulted once
    /// <see cref="ReportsItemFailures"/> is true.
    /// </summary>
    public BatchFailureMode FailureMode => BatchFailureMode.PerItem;

    public IReadOnlyList<int> FailedItems => _failed;

    public void RecordFailure(int index, Exception failure) => _failed.Add(index);

    /// <summary>
    /// The request for one message in the batch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mapping from a message to a request, which is adapter knowledge: that an application
    /// property is a header and a lock token is not. The application properties become headers
    /// under their own names, and the two facts Service Bus carries outside them get prefixed
    /// names so a property called <c>messageId</c> cannot collide with the message id.
    /// </para>
    /// <para>
    /// String properties as they are, and the other primitives the AMQP map allows rendered
    /// invariantly; a binary property is not a header and is left out, as a binary SQS attribute
    /// is. The content type travels as <c>Content-Type</c> when the publisher set one, because it
    /// is the one message property that already means what a header of that name means.
    /// </para>
    /// </remarks>
    public IExecutionRequest ForMessage(ServiceBusReceivedMessage message) {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in message.ApplicationProperties) {
            var rendered = Render(property.Value);

            if (rendered != null) {
                headers[property.Key] = rendered;
            }
        }

        Set(headers, MessageIdHeader, message.MessageId);
        headers[DeliveryCountHeader] = message.DeliveryCount.ToString(CultureInfo.InvariantCulture);
        Set(headers, "Content-Type", message.ContentType);

        return new FunctionsPayloadRequest(Method, Path, BodyStream(message), headers);
    }

    private static string? Render(object? value) =>
        value switch {
            null => null,
            string text => text,
            byte[] => null,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    private static void Set(IDictionary<string, StringValues> headers, string name, string? value) {
        if (!string.IsNullOrEmpty(value)) {
            headers[name] = value;
        }
    }

    /// <summary>
    /// The message body as a stream. Empty rather than null for an empty message, so a handler
    /// binding a body sees nothing to bind rather than a null reference.
    /// </summary>
    private static Stream BodyStream(ServiceBusReceivedMessage message) {
        var body = message.Body;

        return body == null || body.ToMemory().IsEmpty
            ? Stream.Null
            : body.ToStream();
    }
}

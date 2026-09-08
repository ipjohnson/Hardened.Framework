using Azure.Messaging.EventHubs;
using Hardened.Azure.Functions.EventHubs;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Azure.Functions.Testing;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing.Conformance;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Azure.Functions.Runtime.Tests.Conformance;

/// <summary>
/// The request a stream handler actually meets, held to the payload-shaped profile.
/// </summary>
/// <remarks>
/// The per-event fork rather than the batch request, for the reason the Service Bus enrolment
/// gives: the fork is what reaches a handler, and the batch exists to be forked. The spec's headers
/// arrive as the event's properties, which is the only header-like channel an event has, and the
/// body as the event body.
/// </remarks>
public class EventHubsRequestConformanceTests : PayloadExecutionRequestConformanceTests {
    protected override IExecutionRequestConformanceAdapter Adapter { get; } = new EventHubsAdapter_();

    private sealed class EventHubsAdapter_ : IExecutionRequestConformanceAdapter {
        private readonly EventHubsAdapter _adapter = new();

        public string TransportName => "Azure Functions Event Hubs";

        public IExecutionRequest CreateRequest(ConformanceRequestSpec spec) {
            var eventData = EventHubsModelFactory.EventData(
                eventBody: spec.Body == null ? new BinaryData(Array.Empty<byte>()) : BinaryData.FromBytes(spec.Body),
                properties: spec.Headers.ToDictionary(header => header.Key, header => (object)header.Value),
                sequenceNumber: 1,
                offset: 64,
                enqueuedTime: DateTimeOffset.UtcNow);

            var context = new TestFunctionContext(
                "Stream_conformance",
                new Dictionary<string, object?>(),
                new ServiceCollection().BuildServiceProvider());

            var batch = (EventHubsRequest)_adapter.CreateRequest(
                new FunctionsTrigger("STREAM", "/conformance", new[] { eventData }), context);

            // Method and path come off the shim rather than the spec - STREAM and the hub's name -
            // so the spec's are applied through Clone, the same door a filter uses.
            return batch.ForEvent(batch.Events[0]).Clone(method: spec.Method, path: spec.Path);
        }
    }
}

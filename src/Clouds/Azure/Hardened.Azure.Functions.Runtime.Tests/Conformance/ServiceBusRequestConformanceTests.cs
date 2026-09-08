using Azure.Messaging.ServiceBus;
using Hardened.Azure.Functions.Runtime.Execution;
using Hardened.Azure.Functions.ServiceBus;
using Hardened.Azure.Functions.Testing;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing.Conformance;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Azure.Functions.Runtime.Tests.Conformance;

/// <summary>
/// The request a queue handler actually meets, held to the payload-shaped profile.
/// </summary>
/// <remarks>
/// <para>
/// The per-message fork rather than the batch request, for the reason the SQS enrolment gives:
/// the fork is what reaches a handler, and the batch exists to be forked.
/// </para>
/// <para>
/// The spec's headers arrive as application properties, which is the only header-like channel a
/// Service Bus message has, and the body as the message body.
/// </para>
/// </remarks>
public class ServiceBusRequestConformanceTests : PayloadExecutionRequestConformanceTests {
    protected override IExecutionRequestConformanceAdapter Adapter { get; } = new ServiceBusAdapter_();

    private sealed class ServiceBusAdapter_ : IExecutionRequestConformanceAdapter {
        private readonly ServiceBusAdapter _adapter = new();

        public string TransportName => "Azure Functions Service Bus";

        public IExecutionRequest CreateRequest(ConformanceRequestSpec spec) {
            var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: spec.Body == null ? null : BinaryData.FromBytes(spec.Body),
                messageId: "conformance",
                properties: spec.Headers.ToDictionary(header => header.Key, header => (object)header.Value),
                deliveryCount: 1);

            var context = new TestFunctionContext(
                "Queue_conformance",
                new Dictionary<string, object?>(),
                new ServiceCollection().BuildServiceProvider());

            var batch = (ServiceBusRequest)_adapter.CreateRequest(
                new FunctionsTrigger("QUEUE", "/conformance", new[] { message }), context);

            // Method and path come off the shim rather than the spec - QUEUE and the queue's name -
            // so the spec's are applied through Clone, the same door a filter uses. The same
            // concession the SQS enrolment makes, and for the same reason.
            return batch.ForMessage(batch.Messages[0]).Clone(method: spec.Method, path: spec.Path);
        }
    }
}

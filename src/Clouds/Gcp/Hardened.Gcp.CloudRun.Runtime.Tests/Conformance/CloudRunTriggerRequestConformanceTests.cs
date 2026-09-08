using Hardened.Gcp.CloudRun.PubSub;
using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Gcp.CloudRun.Testing;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Requests.Testing.Conformance;
using Microsoft.Extensions.Primitives;

namespace Hardened.Gcp.CloudRun.Runtime.Tests.Conformance;

/// <summary>
/// The request a queue handler meets on Cloud Run, held to the payload-shaped profile.
/// </summary>
/// <remarks>
/// <para>
/// The trigger request the push envelope builds, not the web-shaped delivery it arrived in: the
/// delivery is a POST with a JSON body and carries none of the handler's headers, and holding it
/// to a profile written for a message would assert the wrong thing.
/// </para>
/// <para>
/// The spec's headers arrive as message attributes, which is the only header-like channel a push
/// has, and its body as the base64 data. Method and path come off the envelope - QUEUE and the
/// subscription's name - so the spec's are applied through <c>Clone</c>, the same door a filter
/// uses and the same concession the SQS enrolment makes.
/// </para>
/// </remarks>
public class CloudRunTriggerRequestConformanceTests : PayloadExecutionRequestConformanceTests {
    protected override IExecutionRequestConformanceAdapter Adapter { get; } = new PushAdapter();

    private sealed class PushAdapter : IExecutionRequestConformanceAdapter {
        private readonly PubSubPushEnvelope _envelope = new();

        public string TransportName => "Cloud Run Pub/Sub push";

        public IExecutionRequest CreateRequest(ConformanceRequestSpec spec) {
            var push = PubSubPush.Body(
                CloudRunEnvelopeDelivery.Subscription("conformance"),
                spec.Body ?? Array.Empty<byte>(),
                attributes: new Dictionary<string, string>(spec.Headers),
                messageId: "conformance",
                publishTime: CloudRunEnvelopeDelivery.PublishTime);

            var delivery = new TestExecutionRequest("POST", "/", null, EmptyQueryStringCollection.Instance) {
                Headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) {
                    ["Content-Type"] = "application/json"
                },
                Body = new MemoryStream(push, writable: false)
            };

            using var payload = new TriggerPayload(push);

            var trigger = _envelope.Unwrap(delivery, payload)
                          ?? throw new InvalidOperationException("The envelope declined the push built for it.");

            return trigger.Clone(method: spec.Method, path: spec.Path);
        }
    }
}

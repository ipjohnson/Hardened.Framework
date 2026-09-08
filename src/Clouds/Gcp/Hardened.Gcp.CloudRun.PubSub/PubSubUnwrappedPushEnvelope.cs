using Hardened.Gcp.CloudRun.Runtime.Envelopes;
using Hardened.Gcp.CloudRun.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Gcp.CloudRun.PubSub;

/// <summary>
/// A Pub/Sub push with payload unwrapping and metadata writing on: the message's data as the whole
/// body, and everything else as headers.
/// </summary>
/// <remarks>
/// <para>
/// A subscription created with <c>--push-payload-unwrap</c> posts the raw data rather than the
/// JSON envelope, and with metadata writing on Pub/Sub adds <c>x-goog-pubsub-subscription-name</c>,
/// <c>x-goog-pubsub-message-id</c>, <c>x-goog-pubsub-publish-time</c> and
/// <c>x-goog-pubsub-ordering-key</c>, with each message attribute as a header of its own. That is
/// the wrapped form's headers already, so the request a handler meets is the same as for a wrapped
/// push, with the body having been handed on as it arrived.
/// </para>
/// <para>
/// Recognised by the subscription header, which is also what routes it. An unwrapped push without
/// metadata writing carries nothing that says it is one - it is a POST with a body - and cannot
/// be recognised; a subscription that wants unwrapping has to write the metadata too.
/// </para>
/// <para>
/// Every header of the delivery is copied, because Pub/Sub puts the attributes among them under
/// their own names and nothing marks which headers are attributes.
/// </para>
/// </remarks>
public sealed class PubSubUnwrappedPushEnvelope : ITriggerEnvelope {
    public bool Recognises(IExecutionRequest request) =>
        string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
        TriggerHeaders.Get(request.Headers, PubSubPushBody.SubscriptionHeader) != null;

    public CloudRunTriggerRequest? Unwrap(IExecutionRequest request, TriggerPayload payload) {
        var subscription = TriggerHeaders.Get(request.Headers, PubSubPushBody.SubscriptionHeader);

        if (string.IsNullOrEmpty(subscription)) {
            return null;
        }

        return new CloudRunTriggerRequest(
            PubSubPushEnvelope.QueueScheme,
            "/" + PubSubPushBody.SubscriptionName(subscription!),
            payload.AsStream(),
            TriggerHeaders.Copy(request.Headers),
            request);
    }
}

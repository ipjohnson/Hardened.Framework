# Queues

A queue handler receives one message at a time. Pub/Sub pushes one message per request, so there
is no batch to unpack: the request is the message.

```csharp
using Hardened.Functions.Runtime.Attributes;

public class OrderHandlers {

    [Queue("orders")]
    public void OnOrder(Order order, IOrderStore store) => store.Place(order);
}
```

Nothing above names Pub/Sub, and the application module names only the host:

```csharp
[HardenedModule]
[CloudRunRuntime]
public partial class Application;
```

Referencing `Hardened.Gcp.CloudRun.PubSub` is what decides that `[Queue]` means a push
subscription. See [Triggers](/guide/triggers) for the vocabulary.

## Packages

```xml
<PackageReference Include="Hardened.Gcp.CloudRun.Runtime" Version="0.34.0-rc1000" />
<PackageReference Include="Hardened.Gcp.CloudRun.PubSub" Version="0.34.0-rc1000" />
```

The same package serves `[Topic]`, from a different delivery; see [Topics](/gcp/topic).

`dotnet new hardened-function --host gcp --trigger queue` writes this shape with tests.

## The subscription is the queue

`[Queue("orders")]` routes on the name of the push subscription that delivered the message, not on
the topic's. A push body carries the subscription's full name and nothing about the topic, so the
subscription is the only thing the adapter can route on. Name the subscription what the handler
names:

```bash
gcloud pubsub subscriptions create orders \
    --topic=orders \
    --push-endpoint="https://orders-abc123-uc.a.run.app/" \
    --push-auth-service-account=pubsub-pusher@my-project.iam.gserviceaccount.com
```

The endpoint is the service's root. The front door recognises a push by its shape rather than by
its path, so nothing in the URL has to name the queue.

Two subscriptions on one topic are two queues, and a handler for each. Two services sharing one
subscription is not a thing Pub/Sub does, and it is not how a topic fans out here; a topic is an
Eventarc trigger, see [Topics](/gcp/topic).

## What the handler sees

The message's `data` is base64 in the push body. The adapter decodes it, and the decoded bytes are
the request body the handler's parameter is bound from, so a JSON message binds the way a JSON web
request does. The message's attributes become request headers, named as the attributes were named.
Beside them, the delivery's own metadata:

| Header | Carries |
|---|---|
| `x-goog-pubsub-subscription-name` | The subscription's full resource name |
| `x-goog-pubsub-message-id` | The message id, the same on every redelivery |
| `x-goog-pubsub-publish-time` | When the message was published |
| `x-goog-pubsub-ordering-key` | The ordering key, when the message has one |
| `x-goog-pubsub-delivery-attempt` | The attempt count, when the subscription has a dead-letter policy |

The metadata is written after the attributes, so an attribute named like one of these does not
replace it.

A subscription with payload unwrapping turned on sends the message's bytes as the request body and
the same metadata in the same headers. The adapter recognises that form by the subscription header
and routes it the same way, so a service does not care which the deployment chose.

A push larger than 16 MiB is answered 413 without being read, because Pub/Sub does not send one.

## What a failure means

Returning normally answers the push with a success status, which acknowledges the message.
Throwing answers 500, which Pub/Sub reads as a negative acknowledgement, so the message is
redelivered until it is acknowledged or the subscription's dead-letter policy moves it on. There is
no other way to say "not handled", and no batch report: one message, one answer.

That is `PerItem` in the framework's [vocabulary](/guide/triggers#batches-and-what-a-failure-means)
with a batch of one, and there is no `ReportBatchItemFailures` to set, because Pub/Sub has nothing
to report to. The module is never written out.

Redelivery uses the subscription's acknowledgement deadline. A handler that outlives it sees the
same message arrive again on another instance while it is still running, so keep queue handlers
shorter than the deadline, or make them idempotent, or both.

## Testing

The generated façade sends through the real pipeline. Passing several payloads sends several
pushes:

```csharp
[HardenedTest]
public async Task EveryMessageIsHandled(Application.Queues queues, OrderLog log) {
    await queues.Orders(
        new Order { Id = "A-1" }, new Order { Id = "A-2" }, new Order { Id = "A-3" });

    Assert.Equal(3, log.Orders.Count);
}
```

The method name is the queue's, so `[Queue("orders-new")]` is `queues.OrdersNew`. It exists because
the handler does, and its parameter is the type the handler binds.

Adding `[assembly: CloudRunTesting]` beside `[assembly: WebTesting]` builds the push Pub/Sub
actually sends and posts it to the test's host, so the front door, the decoding and the metadata
headers are exercised too. No test method changes. See
[Testing Cloud Run handlers](/gcp/testing).

## Next

- [Topics](/gcp/topic): the same package, the other Pub/Sub delivery
- [Triggers](/guide/triggers): the failure semantics in full
- [Testing Cloud Run handlers](/gcp/testing): the four fidelity levels

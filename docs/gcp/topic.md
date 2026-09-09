# Topics

A topic fans out. Every service with a trigger on the topic sees every message, and the handler is
written the way a queue handler is:

```csharp
using Hardened.Functions.Runtime.Attributes;

public class OrderEventHandlers {

    [Topic("order-events")]
    public void OnOrderEvent(OrderEvent published, IProjection projection) =>
        projection.Apply(published);
}
```

## Packages

```xml
<PackageReference Include="Hardened.Gcp.CloudRun.Runtime" Version="0.32.0-rc1000" />
<PackageReference Include="Hardened.Gcp.CloudRun.PubSub" Version="0.32.0-rc1000" />
```

`dotnet new hardened-function --host gcp --trigger topic` writes this shape with tests.

## The topic is an Eventarc trigger

`[Topic("order-events")]` routes on the name of the topic, and the only Pub/Sub delivery that
carries the topic's name is the one Eventarc sends. An Eventarc trigger on a topic delivers each
message as a CloudEvent whose `ce-source` names the topic and whose `ce-type` is
`google.cloud.pubsub.topic.v1.messagePublished`. The adapter routes on the source:

```bash
gcloud eventarc triggers create order-events \
    --location=us-central1 \
    --destination-run-service=projections \
    --event-filters="type=google.cloud.pubsub.topic.v1.messagePublished" \
    --transport-topic=projects/my-project/topics/order-events \
    --service-account=eventarc-invoker@my-project.iam.gserviceaccount.com
```

Eventarc creates the subscription it needs, and its name is not something the handler sees. A
second service with a trigger on the same topic gets a subscription of its own and every message,
which is what makes this a topic.

A plain push subscription cannot serve `[Topic]`, because a push body names the subscription and
not the topic. That is the whole reason the two attributes deploy differently here, and it is why a
subscription named after a topic is not accepted as a stand-in: the handler would be routing on a
naming rule the deployment has to know and the code cannot check. Use `[Queue]` for a push
subscription; see [Queues](/gcp/queue).

## What the handler sees

The CloudEvent's data is the same push body a subscription sends, with the message inside it. The
adapter decodes the message's `data` into the request body, writes the attributes and the
`x-goog-pubsub-*` metadata as headers the way the queue adapter does, and keeps the CloudEvent's own
attributes on the request as `ce-id`, `ce-source`, `ce-type`, `ce-time` and the rest. Binary and
structured CloudEvents are both read.

## What a failure means

The same as a queue: a success answer acknowledges, a thrown exception answers 500 and Eventarc
redelivers, with the retry policy of the subscription Eventarc created. One message per request, no
batch and nothing to report.

## Testing

```csharp
[HardenedTest]
public async Task ANotificationReachesTheHandler(Application.Topics topics, IProjection projection) {
    await topics.OrderEvents(new OrderEvent { Id = "A-1" });

    // ...
}
```

`Topics` for `[Topic]`, with a method per topic. Adding `[assembly: CloudRunTesting]` builds the
binary-mode CloudEvent Eventarc actually sends, with the push body inside it, and posts it to the
test's host. See [Testing Cloud Run handlers](/gcp/testing).

## Next

- [Queues](/gcp/queue): the push subscription form
- [Events](/gcp/event): any other CloudEvent Eventarc delivers
- [Triggers](/guide/triggers): the vocabulary

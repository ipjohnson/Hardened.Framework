# Topics

`[Topic("orders")]` on a method makes it the handler for messages published to the Pub/Sub topic
named `orders`. An Eventarc trigger on the topic delivers each message to the service as one HTTP
request, and the handler runs once for each request.

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Topic("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

In the `hardened-function` template, `Order` has a string `Id` and an int `Quantity`. `OrderLog` is
a `[SingletonService]` that keeps the orders it is given.

The application class names `[CloudRunRuntime]` and no adapter module:

```csharp
using Hardened.Gcp.CloudRun.Runtime;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[CloudRunRuntime]
public partial class Application;
```

The same files run as a Cloud Functions 2nd gen function. The function answers the delivery and the
failures on this page with the same statuses as the service. Its handler receives the same headers.
[Triggers](/guide/triggers) covers `[Topic]` and the other trigger attributes.

## Packages

A topic service references the adapter package `Hardened.Gcp.CloudRun.PubSub` beside
`Hardened.Gcp.CloudRun.Runtime`:

```xml
<PackageReference Include="Hardened.Gcp.CloudRun.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Gcp.CloudRun.PubSub" Version="0.0.0-HARDENED-VERSION" />
```

The adapter package makes `[Topic]` mean a Pub/Sub topic. The build registers its `PubSubModule` on
the application, so the application does not need to declare the module. The module has no
settings.

The adapter package also serves `[Queue]`, from a push subscription. [Queues](/gcp/queue) covers it.
A Cloud Functions 2nd gen function references the same adapter package. [Web services](/gcp/web)
covers the function's own packages. The other packages, `Program.cs` and the project settings are
the same for every Google Cloud service. The Google Cloud [Overview](/gcp/) covers them.

The `hardened-function` template writes this service and a test project:

```bash
dotnet new hardened-function -n Orders --host gcp --trigger topic
```

It writes the handler and the application class above as `src/Orders/OrderHandler.cs` and
`src/Orders/Application.cs`.

## Routing by topic name

The adapter reads a `POST` as a topic's message when its `ce-type` header is
`google.cloud.pubsub.topic.v1.messagePublished`. It also reads the same event in structured mode, as
one `application/cloudevents+json` document. Eventarc delivers a message published to a topic as a
CloudEvent of that type, and the event's `ce-source` names the topic
([CloudEvents format](https://docs.cloud.google.com/eventarc/docs/cloudevents)).

The adapter takes the topic's name from the last segment of `ce-source`. The source
`//pubsub.googleapis.com/projects/my-project/topics/orders` reaches `[Topic("orders")]`, under the
route `TOPIC /orders`. The name matches exactly, including case. The request's path does not change
the route. Eventarc posts to the service's root path, `/`, unless the trigger sets another
([Create triggers with Eventarc](https://docs.cloud.google.com/run/docs/triggering/trigger-with-events)).

A delivery from a topic that no handler names answers 500. For the topic `payments`, the log says:

```text
No handler is registered for TOPIC /payments. An event source is wired to this function that no trigger attribute declared.
```

A Pub/Sub push subscription does not reach a `[Topic]` handler. Its request names the subscription
and not the topic. The adapter routes it under `QUEUE` with the subscription's name, such as
`QUEUE /orders`. One service can serve `[Topic("orders")]` and `[Queue("orders")]`. They are two
routes, `TOPIC /orders` and `QUEUE /orders`. Each delivery reaches its own handler.
[Queues](/gcp/queue) covers push subscriptions.

## Message body and headers

The handler receives the parts of the Eventarc delivery as this table shows:

| From the Eventarc delivery | Reaches the handler as |
|---|---|
| `message.data` | The request body, decoded from base64 |
| `message.attributes` | A header for each attribute, named for it |
| `message.messageId` | `x-goog-pubsub-message-id` |
| `message.publishTime` | `x-goog-pubsub-publish-time` |
| `message.orderingKey` | `x-goog-pubsub-ordering-key`, when the message has one |
| `subscription` | `x-goog-pubsub-subscription-name`, the subscription Eventarc created |
| `deliveryAttempt` | `x-goog-pubsub-delivery-attempt`, when the delivery carries it |
| `ce-id`, `ce-source`, `ce-specversion`, `ce-type`, `ce-time` | The same headers. `ce-source` also gives the route |
| The request's other headers | Nothing |

The body holds nothing else from the delivery. [Triggers](/guide/triggers) covers how the body binds
to the handler's parameter.

Any other CloudEvent attribute that the event carries also becomes a header under its `ce-` name. A
message attribute named like an `x-goog-pubsub-*` header does not replace that header. A message
attribute named like a `ce-` header replaces it. The route still comes from the event's own
`ce-source`. The request's own headers, such as `Authorization`, `Content-Type` and `User-Agent`, do
not reach the handler. Header names match without regard to case.

A handler reads the headers through an `IExecutionRequest` parameter. The interface is in
`Hardened.Requests.Abstract.Execution`. A header can be missing. A message can have no attributes.
Under `[FunctionTesting]` alone, the request has no headers. This handler reads two headers with
`TryGetValue`:

```csharp
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Topic("orders")]
    public void OnOrder(Order order, IExecutionRequest request)
    {
        request.Headers.TryGetValue("x-goog-pubsub-message-id", out var messageId);
        request.Headers.TryGetValue("tenant", out var tenant);

        logger.LogInformation(
            "Message {MessageId} for {Tenant}",
            messageId.ToString(),
            tenant.ToString()
        );

        log.Record(order);
    }
}
```

For a message published to the topic `orders`, Eventarc sends this request:

```http
POST /
Content-Type: application/json; charset=utf-8
ce-specversion: 1.0
ce-type: google.cloud.pubsub.topic.v1.messagePublished
ce-source: //pubsub.googleapis.com/projects/my-project/topics/orders
ce-id: 1096434104173400
ce-time: 2026-09-24T12:00:00.000Z

{
  "subscription": "projects/my-project/subscriptions/eventarc-us-central1-orders-sub-102",
  "message": {
    "attributes": {
      "tenant": "acme"
    },
    "data": "eyJpZCI6IkEtMSIsInF1YW50aXR5IjoyfQ==",
    "messageId": "1096434104173400",
    "publishTime": "2026-09-24T12:00:00.000Z"
  }
}

HTTP/1.1 200 OK
```

Eventarc sends these `ce-` headers and this `Content-Type`. The request body is the Pub/Sub push
body, and the message's data is in it as base64. The data `eyJpZCI6IkEtMSIsInF1YW50aXR5IjoyfQ==` is
`{"id":"A-1","quantity":2}`, which the handler receives as its `Order`. The handler logs
`Message 1096434104173400 for acme`. The service answers 200 with no body.

The subscription is the one Eventarc created for the trigger. Its ID begins with `eventarc-` and the
trigger's region. The Google Cloud [Overview](/gcp/) covers running the service locally and posting
a delivery to it.

## Failures and redelivery

A message fails when its handler throws, when its data does not bind to the handler's parameter, or
when no handler names its topic. The request answers with the status in this table:

| What happened | The request answers | Pub/Sub reads it as |
|---|---|---|
| The handler returned | 200, no body | An acknowledgement |
| The handler threw | 500, `{"type":"ServerError","message":"The server could not complete this request.","details":""}` | A negative acknowledgement |
| The message's data did not bind to the parameter | 400, a `ValidationError` body naming the parameter | A negative acknowledgement |
| No handler names the topic | 500, no body | A negative acknowledgement |

Pub/Sub carries Eventarc's delivery. It reads 102, 200, 201, 202 and 204 as an acknowledgement. It
reads any other status as a negative acknowledgement and sends the message again
([Push subscriptions](https://docs.cloud.google.com/pubsub/docs/push)). A request carries no batch
of messages, so its response has no report of failed messages. The adapter package's module has no
setting that reports failures.

A trigger created with `gcloud eventarc triggers create` retries a failed delivery by default. The
retries use an exponential backoff that starts at 10 seconds and grows to at most 600 seconds. The
dead-letter topic and the other retry settings belong to the subscription that Eventarc created for
the trigger. `gcloud eventarc triggers describe` names that subscription
([Retry events](https://docs.cloud.google.com/eventarc/docs/retry-events)).

`--max-retry-attempts=1` on the trigger makes one delivery attempt with no retries. It is the only
value the flag takes
([gcloud eventarc triggers create](https://docs.cloud.google.com/sdk/gcloud/reference/eventarc/triggers/create)).
A function deployed with `gcloud functions deploy --trigger-topic` gets its own Eventarc trigger
([gcloud functions deploy](https://docs.cloud.google.com/sdk/gcloud/reference/functions/deploy)).

::: warning
A message whose handler keeps failing is lost. Eventarc keeps an undelivered message for 24 hours by
default. After that it discards the message, unless the subscription Eventarc created has a
dead-letter topic. For a function deployed with `gcloud functions deploy --trigger-topic` and no
`--retry`, Eventarc delivers the message only once
([REST Resource: projects.locations.functions](https://docs.cloud.google.com/functions/docs/reference/rest/v2/projects.locations.functions)).
:::

The subscription Eventarc creates has an acknowledgement deadline of 10 seconds by default. When the
deadline passes before the service answers, Pub/Sub sends the message again. A handler that runs
longer than the deadline can run twice for one message. Google recommends a deadline of 600
seconds, the maximum
([Create triggers with Eventarc](https://docs.cloud.google.com/run/docs/triggering/trigger-with-events)).

Eventarc can deliver a message more than once, even when no handler failed. Google recommends the
event's id as the key for handling a message once. The event's `ce-id` is the message's id.

## Connecting the topic to the service

An Eventarc trigger connects the topic to the service. The trigger names the event type
`google.cloud.pubsub.topic.v1.messagePublished`, the topic in `--transport-topic` and the service in
`--destination-run-service`. The trigger calls the service as its service account. That account
needs the Cloud Run Invoker role, `roles/run.invoker`, on the service
([Route Cloud Pub/Sub events to Cloud Run](https://docs.cloud.google.com/eventarc/standard/docs/run/route-trigger-cloud-pubsub)).

In these commands, `orders` is the deployed service and `my-project` is the project.
`eventarc-invoker` is a service account of your choosing.

```bash
gcloud run services add-iam-policy-binding orders --region=us-central1 \
    --member=serviceAccount:eventarc-invoker@my-project.iam.gserviceaccount.com \
    --role=roles/run.invoker

gcloud eventarc triggers create orders \
    --location=us-central1 \
    --destination-run-service=orders \
    --event-filters="type=google.cloud.pubsub.topic.v1.messagePublished" \
    --transport-topic=projects/my-project/topics/orders \
    --service-account=eventarc-invoker@my-project.iam.gserviceaccount.com
```

The first command sends `setIamPolicy` for the service `orders`, with a binding of
`roles/run.invoker` for `serviceAccount:eventarc-invoker@my-project.iam.gserviceaccount.com`. The
second sends `POST /v1/projects/my-project/locations/us-central1/triggers?triggerId=orders`. That
request names the destination service `orders` in `us-central1`, the event filter
`type=google.cloud.pubsub.topic.v1.messagePublished`, the service account and the transport topic
`projects/my-project/topics/orders`. It sets no retry policy. The second command then prints:

```text
Publish to Pub/Sub topic [projects/my-project/topics/orders] to receive events in Cloud Run service [orders].
```

A new trigger can take up to two minutes to deliver events
([Create triggers with Eventarc](https://docs.cloud.google.com/run/docs/triggering/trigger-with-events)).

The topic in `--transport-topic` is the topic named in `[Topic]`. It must be in the same project as
the trigger. Without `--transport-topic`, Eventarc creates a new topic for the trigger
([gcloud eventarc triggers create](https://docs.cloud.google.com/sdk/gcloud/reference/eventarc/triggers/create)).
For a Cloud Functions 2nd gen function, `--destination-run-service` names the function. The function
is a Cloud Run service
([Invoke with an HTTPS Request](https://docs.cloud.google.com/run/docs/triggering/https-request)).
[Failures and redelivery](#failures-and-redelivery) covers the trigger's retries and its dead-letter
topic.

The service deploys the same way for every trigger. The Google Cloud [Overview](/gcp/) covers
deploying it. [Web services](/gcp/web) covers deploying a function.

## Testing

The `hardened-function` template writes this test as `tests/Orders.Tests/OrderHandlerTests.cs`. It
sends a message through `Application.Topics`.

```csharp
using DependencyModules.xUnit.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderHandlerTests
{
    [ModuleTest]
    public async Task ANotificationReachesTheHandler(Application.Topics topics, OrderLog log)
    {
        await topics.Orders(new Order { Id = "A-1", Quantity = 2 });

        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

The template's test project declares `[assembly: CloudRunTesting]` and `[assembly: WebTesting]`.
`[CloudRunTesting]` sends each message of a façade call as its own Eventarc request, posted to the
test's host. Each request is a binary-mode CloudEvent posted to `/`, with these values:

| Field | Value |
|---|---|
| `ce-id` and the message id | The topic's name and the message's position: `orders-0`, `orders-1` |
| `ce-source` | `//pubsub.googleapis.com/projects/test-project/topics/orders` |
| The subscription | `projects/test-project/subscriptions/eventarc-orders` |
| `ce-time` and the publish time | `2026-01-01T00:00:00.000Z` |
| Attributes | None |

Under `[FunctionTesting]` alone, the request has no headers. The handler that reads two headers
passes the template's test under both.

A call with several messages handles a failed message according to the attribute:

| Assembly attribute | A call with several messages, when one of them fails |
|---|---|
| `[CloudRunTesting]` | The call delivers every message, then fails with the first failed message's exception |
| `[FunctionTesting]` alone | The call fails at the first failed message, and the messages after it do not run |

[Testing functions](/guide/testing-functions) covers the façades and the deliveries. The Google
Cloud [Testing](/gcp/testing) page covers `[CloudRunTesting]`.

## Next

| Page | Covers |
|---|---|
| [Queues](/gcp/queue) | Pub/Sub push subscriptions, the other delivery of the same package |
| [Triggers](/guide/triggers) | The trigger attributes, source names and payload binding |
| [Overview](/gcp/) | The Google Cloud packages, `Program.cs`, running locally and deploying a service |
| [Web services](/gcp/web) | Running the same application as a Cloud Functions 2nd gen function |
| [Testing functions](/guide/testing-functions) | Testing a trigger handler |

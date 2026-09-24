# Queues

`[Queue("orders")]` on a method makes the method the handler for the Pub/Sub push subscription named
`orders`. Pub/Sub sends each message as an HTTPS request of its own, and the handler runs once for
each message.

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Queue("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

This push from the subscription `orders` reaches the handler:

```http
POST /
Content-Type: application/json

{
  "message": {
    "data": "eyJpZCI6IkEtMSIsInF1YW50aXR5IjoyfQ==",
    "messageId": "2070443601311540",
    "message_id": "2070443601311540",
    "publishTime": "2021-02-26T19:13:55.749Z",
    "publish_time": "2021-02-26T19:13:55.749Z"
  },
  "subscription": "projects/my-project/subscriptions/orders"
}

HTTP/1.1 200 OK
```

The function answers 200 when the handler returns. The 200 acknowledges the message. The same push
reaches the handler on Cloud Run and on Cloud Functions 2nd gen.

The push's `data` is the message, base64-encoded. Here it is `{"id":"A-1","quantity":2}`. Pub/Sub
writes `messageId` and `publishTime` twice, once in snake case.

In the template, `Order` has a string `Id` and an int `Quantity`. `OrderLog` is a
`[SingletonService]` that keeps the orders it is given.

[Triggers](/guide/triggers) covers `[Queue]` and the other trigger attributes. The Google Cloud
[Overview](/gcp/) covers running the function locally.

## Packages

A queue function references `Hardened.Gcp.CloudRun.PubSub` beside `Hardened.Gcp.CloudRun.Runtime`:

```xml
<PackageReference Include="Hardened.Gcp.CloudRun.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Gcp.CloudRun.PubSub" Version="0.0.0-HARDENED-VERSION" />
```

The adapter package `Hardened.Gcp.CloudRun.PubSub` makes `[Queue]` mean a Pub/Sub push subscription.
The build registers the package's `PubSubModule` on the application. The module has no settings.
Each push answers for its own message, so there is no report of failed messages to turn on.

The same package serves `[Topic]`, which [Topics](/gcp/topic) covers. A Cloud Functions 2nd gen
function references the same adapter package. [Hosts](/guide/hosts) shows the packages of a Cloud
Functions 2nd gen function. [Web services](/gcp/web) covers running the application as a function.
The Google Cloud [Overview](/gcp/) covers the other packages, the application class and the entry
point.

The `hardened-function` template writes this function and a test project:

```bash
dotnet new hardened-function -n Orders --host gcp --trigger queue
```

## Routing

The adapter takes the queue's name from the last segment of the push's `subscription`. A push from
`projects/my-project/subscriptions/orders` reaches `[Queue("orders")]`, under the route
`QUEUE /orders`. The name matches exactly, including case. A name with a dot routes like any other
name. `[Queue("orders.v2")]` receives the pushes of `projects/my-project/subscriptions/orders.v2`.

A push names the subscription and not the topic, so the topic's name never reaches the handler.
Pub/Sub delivers each message to every subscription on its topic. Two subscriptions on one topic are
two queues, each with a handler named for it.

The path that a push is posted to does not matter. The adapter recognises a push by its body.

A push from a subscription that no handler names answers 500. For the subscription `payments`, the
service logs an `InvalidOperationException` with this message:

```text
No handler is registered for QUEUE /payments. An event source is wired to this function that no trigger attribute declared.
```

A service with a `[HardenedFunction]` handler that has no name runs that handler for such a push
instead, and the push is acknowledged. [Invocations](/gcp/invoke) covers it.

An Eventarc trigger on a topic delivers a CloudEvent. The CloudEvent routes as `TOPIC /{topic}` and
reaches a `[Topic]` handler, not a `[Queue]` handler. [Topics](/gcp/topic) covers it.

In a service that also references `Hardened.Gcp.CloudRun.Storage`, a push whose message has the
attributes `eventType` and `bucketId` is read as a Cloud Storage notification. The notification
routes as `BLOB /{bucketId}`, whatever its subscription. [Blobs](/gcp/blob) covers the notification.

## Request body

The adapter base64-decodes the message's `data`. The decoded bytes are the request body.
[Triggers](/guide/triggers) covers how the body binds to the handler's parameter.

A message can carry attributes and no data. Its request body is empty. A handler with no payload
parameter receives such a message. For a handler that binds a payload, the push answers 400.

A push whose `data` is not base64 answers 500.

A request larger than 16 MiB is not read as a push. The function answers such a request with 404
when it has a `Content-Length` header, and with 413 when it does not. A Pub/Sub message's data is at
most 10 MB.

## Headers

The adapter turns the push's metadata and the message's attributes into these headers:

| Header | Carries |
|---|---|
| `x-goog-pubsub-subscription-name` | The subscription's full name, such as `projects/my-project/subscriptions/orders` |
| `x-goog-pubsub-message-id` | The message id |
| `x-goog-pubsub-publish-time` | When the message was published |
| `x-goog-pubsub-ordering-key` | The ordering key, when the message has one |
| `x-goog-pubsub-delivery-attempt` | The delivery attempt, when the push carries one |
| The attribute's name | Each message attribute's value |

A redelivered message carries the same message id.

The adapter writes the metadata after the attributes. An attribute with the name of a metadata
header is replaced by the metadata that the push carries.

A handler reads the headers through an `IExecutionRequest` parameter. The interface is in
`Hardened.Requests.Abstract.Execution`. Header names match without regard to case. This version of
`src/Orders/OrderHandler.cs` logs the message id and the `tenant` attribute:

```csharp
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Queue("orders")]
    public void OnOrder(Order order, IExecutionRequest request)
    {
        request.Headers.TryGetValue("x-goog-pubsub-message-id", out var messageId);
        request.Headers.TryGetValue("tenant", out var tenant);

        logger.LogInformation(
            "Message {MessageId} for tenant {Tenant}",
            messageId.ToString(),
            tenant.ToString()
        );

        log.Record(order);
    }
}
```

The handler reads the headers with `TryGetValue`, because a header can be missing. This push carries
the attribute `tenant`:

```http
POST /
Content-Type: application/json

{
  "message": {
    "attributes": {
      "tenant": "acme"
    },
    "data": "eyJpZCI6IkEtMSIsInF1YW50aXR5IjoyfQ==",
    "messageId": "2070443601311540",
    "message_id": "2070443601311540",
    "publishTime": "2021-02-26T19:13:55.749Z",
    "publish_time": "2021-02-26T19:13:55.749Z"
  },
  "subscription": "projects/my-project/subscriptions/orders"
}

HTTP/1.1 200 OK
```

For this push, the handler logs `Message 2070443601311540 for tenant acme`.

`[FromHeader]` does not bind on a trigger handler. [Triggers](/guide/triggers) covers it.

The handler sees none of the headers of the HTTP request that a wrapped push arrives in, such as
`Authorization` or `User-Agent`. On Google Cloud, the request parameter is a
`CloudRunTriggerRequest`, in the namespace `Hardened.Gcp.CloudRun.Runtime.Execution`. Its `Delivery`
property is the HTTP request that the push arrived in, with its method, path and headers.

## Unwrapped pushes

A subscription with payload unwrapping and metadata writing sends the message's data as the whole
body. The metadata and each attribute arrive as headers of the request. An unwrapped push has no
delivery attempt. Its metadata headers are the ones in the [Headers](#headers) table, except
`x-goog-pubsub-delivery-attempt`.

The adapter routes an unwrapped push by `x-goog-pubsub-subscription-name`. The handler binds the
data as it was published. For an unwrapped push, the handler sees every header of the request,
including `Authorization`, `Content-Type` and `User-Agent`.

A subscription with payload unwrapping and without metadata writing sends nothing that the adapter
recognises.

::: warning
A subscription created with `--push-no-wrapper` also needs `--push-no-wrapper-write-metadata`.
Without it, no handler runs. The service answers every push with 404. It logs each 404 at
`Information`, not as an error. Pub/Sub resends each message until it is dead-lettered or expires.
:::

## Failures and redelivery

Pub/Sub reads the status 102, 200, 201, 202 or 204 as an acknowledgement. Pub/Sub reads any other
status as a negative acknowledgement. The function answers each case with the status in this table:

| Case | Status | Pub/Sub |
|---|---|---|
| The handler returns | 200, empty | Acknowledges |
| The handler throws | 500, `{"type":"ServerError","message":"The server could not complete this request.","details":""}` | Resends |
| The data does not bind, or fails a constraint | 400, a `ValidationError` naming the field | Resends |
| The data is not base64 | 500, empty | Resends |
| No handler names the subscription | 500, empty | Resends |
| An unwrapped push without metadata | 404, empty | Resends |

A message that fails binding or validation is resent like a message whose handler throws.

Pub/Sub resends a message after a negative acknowledgement. Pub/Sub also resends a message when the
push is not answered within the subscription's acknowledgement deadline. The acknowledgement
deadline is 10 seconds by default, and at most 600 seconds. By default, Pub/Sub resends a message at
once. A retry policy with exponential backoff waits between 10 and 600 seconds by default.

A subscription with a dead-letter topic forwards a message there after the maximum number of
delivery attempts. The maximum is 5 by default, and between 5 and 100. Pub/Sub counts delivery
attempts only on a subscription with a dead-letter topic. On such a subscription, a push carries the
count. The handler reads the count as `x-goog-pubsub-delivery-attempt`.

A forwarded message carries attributes such as `CloudPubSubDeadLetterSourceDeliveryCount` and
`CloudPubSubDeadLetterSourceSubscription`. A `[Queue]` handler on a push subscription to the
dead-letter topic reads them as headers.

Pub/Sub can deliver a message more than once, even after it was acknowledged. On a subscription with
message ordering, Pub/Sub pushes one message for each ordering key at a time. A redelivered message
brings the later messages of its key again, even acknowledged ones.

Hardened does not repeat a failed message itself. `[Retry]` repeats a trigger handler only with
`AllowNonIdempotent = true`. [The execution pipeline](/guide/execution-pipeline) covers `[Retry]`.

Google Cloud's documentation covers
[acknowledgement](https://docs.cloud.google.com/pubsub/docs/push),
[retry](https://docs.cloud.google.com/pubsub/docs/subscription-retry-policy),
[dead-letter topics](https://docs.cloud.google.com/pubsub/docs/dead-letter-topics) and
[message ordering](https://docs.cloud.google.com/pubsub/docs/ordering).

## Creating the subscription

A push subscription connects a topic to the service. Name the subscription as the handler's
`[Queue]` names the queue. This command creates the subscription `orders` for the example's handler:

```bash
gcloud pubsub subscriptions create orders \
    --topic=orders \
    --push-endpoint=https://orders-abc123-uc.a.run.app/ \
    --push-auth-service-account=pubsub-pusher@my-project.iam.gserviceaccount.com \
    --ack-deadline=600
```

| Flag | Sets |
|---|---|
| `--push-endpoint` | The service's URL. Any path on it works |
| `--push-auth-service-account` | The service account that Pub/Sub signs a token as. Pub/Sub sends the token in `Authorization` |
| `--ack-deadline` | How long Pub/Sub waits for the answer, in seconds. Google's tutorial sets 600 |
| `--push-no-wrapper` and `--push-no-wrapper-write-metadata` | An unwrapped push. See [Unwrapped pushes](#unwrapped-pushes) |
| `--dead-letter-topic` and `--max-delivery-attempts` | A dead-letter topic and the maximum number of delivery attempts. See [Failures and redelivery](#failures-and-redelivery) |

The account that `--push-auth-service-account` names needs the Cloud Run Invoker role,
`roles/run.invoker`, on the service.

This command creates the same subscription as an unwrapped push:

```bash
gcloud pubsub subscriptions create orders \
    --topic=orders \
    --push-endpoint=https://orders-abc123-uc.a.run.app/ \
    --push-auth-service-account=pubsub-pusher@my-project.iam.gserviceaccount.com \
    --push-no-wrapper \
    --push-no-wrapper-write-metadata
```

This command gives the subscription the dead-letter topic `orders-dead`, which
`gcloud pubsub topics create orders-dead` creates first:

```bash
gcloud pubsub subscriptions update orders \
    --dead-letter-topic=orders-dead \
    --max-delivery-attempts=5
```

The Pub/Sub service account needs the publisher role on the dead-letter topic and the subscriber
role on the subscription. The Google Cloud [Overview](/gcp/) covers deploying a service.

Pub/Sub reaches a Cloud Functions 2nd gen function either as a push to its URL or through an
Eventarc trigger. A `[Queue]` function is deployed as an HTTP function. The push subscription's
endpoint is the function's URL. The rest of the subscription is the same as for a service. A
function's Pub/Sub trigger is an Eventarc trigger. Its deliveries reach `[Topic]` handlers, which
[Topics](/gcp/topic) covers. [Web services](/gcp/web) covers deploying a function.

Google Cloud's documentation covers push subscriptions
[to Cloud Run](https://docs.cloud.google.com/run/docs/triggering/pubsub-push) and
[to functions](https://docs.cloud.google.com/run/docs/function-triggers), and
[payload unwrapping](https://docs.cloud.google.com/pubsub/docs/payload-unwrapping).

## Testing the handler

`Application.Queues` is the façade that a test calls. [Testing functions](/guide/testing-functions)
covers it. The template's test project declares `[assembly: CloudRunTesting]` beside
`[assembly: WebTesting]`.

Under `[CloudRunTesting]`, each message of a call is one push, posted to `/` on the test's web host.
For the queue `orders`, the pushes carry these values:

| Field | Value |
|---|---|
| Subscription | `projects/test-project/subscriptions/orders` |
| Message id | The queue's name and the message's position, such as `orders-0` and `orders-1` |
| Publish time | `2026-01-01T00:00:00.000Z` |
| Attributes, ordering key and delivery attempt | None |

Under `[FunctionTesting]` alone, the request has no headers. The handler in [Headers](#headers)
passes its tests under both.

[Testing functions](/guide/testing-functions) covers what a failed message does to the call under
`[CloudRunTesting]`. The Google Cloud [Testing](/gcp/testing) page covers `[CloudRunTesting]`.

## Next

| Page | Covers |
|---|---|
| [Triggers](/guide/triggers) | The trigger attributes, source names and payload binding |
| [Topics](/gcp/topic) | Messages that Eventarc delivers from a topic |
| [Overview](/gcp/) | The packages, the entry point, running locally and deploying |
| [Testing](/gcp/testing) | What `[CloudRunTesting]` builds |
| [Testing functions](/guide/testing-functions) | Testing a trigger handler |

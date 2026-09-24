# Events

`[Event("com.acme.orders", "OrderPlaced")]` on a method makes it the handler for Eventarc events
whose `source` is `com.acme.orders` and whose `type` is `OrderPlaced`. Eventarc delivers each event
to the service as an HTTP request that carries a CloudEvent in binary content mode.

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Event("com.acme.orders", "OrderPlaced")]
    public void OnOrderPlaced(Order order) => log.Record(order);
}
```

In the `hardened-function` template, `Order` has a string `Id` and an int `Quantity`. `OrderLog` is
a `[SingletonService]` that keeps the orders it is given.

The application class in `src/Orders/Application.cs` names only the host:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Gcp.CloudRun.Runtime;

namespace Orders;

[HardenedModule]
[CloudRunRuntime]
public partial class Application;
```

When the project references the adapter package `Hardened.Gcp.CloudRun.Eventarc`, `[Event]` means
Eventarc. The build registers the package's `EventarcModule` on the application. The module has no
settings. An application does not need to declare it.

In binary content mode, the request carries the event's attributes in `ce-` headers and its data as
the body. This event reaches the handler:

```http
POST / HTTP/1.1
Host: localhost:8080
Content-Type: application/json
ce-specversion: 1.0
ce-id: 6a7e8feb-b491-4cf7-a9f1-bf3703467718
ce-source: com.acme.orders
ce-type: OrderPlaced
ce-time: 2026-09-23T12:45:07Z

{"id":"A-1","quantity":2}

HTTP/1.1 200 OK
Content-Length: 0
```

The handler's parameter binds the event's data. Each event is one request. The handler runs once for
each event. [Triggers](/guide/triggers) covers `[Event]` and its two arguments.

## Packages

An event service references `Hardened.Gcp.CloudRun.Eventarc` beside `Hardened.Gcp.CloudRun.Runtime`:

```xml
<PackageReference Include="Hardened.Gcp.CloudRun.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Gcp.CloudRun.Eventarc" Version="0.0.0-HARDENED-VERSION" />
```

The same application serves the same events as a Cloud Functions 2nd gen function. The handler does
not change. The function references `Hardened.Gcp.Functions.Runtime` in place of
`Hardened.Gcp.CloudRun.Runtime`. It keeps `Hardened.Gcp.CloudRun.Eventarc`. The Google Cloud
[Overview](/gcp/) covers the host packages, the entry point and the project settings of both hosts.
It also covers running the service locally.

`dotnet new hardened-function` has no `--trigger` value for events. The service on this page comes
from `dotnet new hardened-function -n Orders --host gcp --trigger queue`, with
`Hardened.Gcp.CloudRun.Eventarc` in place of `Hardened.Gcp.CloudRun.PubSub` in
`src/Orders/Orders.csproj`.

The scaffold manages package versions centrally. Its `Directory.Packages.props` has no line for
`Hardened.Gcp.CloudRun.Eventarc`. The package reference alone fails the restore with `NU1010`:

```text
The PackageReference items Hardened.Gcp.CloudRun.Eventarc do not have corresponding PackageVersion.
```

This line in `Directory.Packages.props`, beside the other `Hardened.Gcp.CloudRun` lines, fixes it:

```xml
<PackageVersion Include="Hardened.Gcp.CloudRun.Eventarc" Version="$(HardenedVersion)" />
```

## Routing

The Eventarc adapter routes an event on its `source` and its `type`. It reads them from the
`ce-source` and `ce-type` headers. The route is `EVENT /{source}/{type}`. An event with
`ce-source: com.acme.orders` and `ce-type: OrderPlaced` reaches
`[Event("com.acme.orders", "OrderPlaced")]` under the route `EVENT /com.acme.orders/OrderPlaced`.
The source and the type match exactly, including case. One service can serve several events, with a
handler for each.

An event that no handler declares answers 500. The service logs an `InvalidOperationException` with
this message:

```text
No handler is registered for EVENT /com.acme.orders/OrderShipped. An event source is wired to this function that no trigger attribute declared.
```

The adapter recognises a POST as an event when the POST carries a `ce-specversion` header, or when
its `Content-Type` is `application/cloudevents+json`. An event posted to any path reaches its
handler. A POST that is neither is served as a web request. A service with no web routes answers it
with 404.

### Source and type names

A Google event's `source` is a resource name that begins with `//`.

| Google event | Its `source` |
|---|---|
| Cloud Storage | `//storage.googleapis.com/projects/_/buckets/BUCKET_NAME` |
| Cloud Audit Logs | `//cloudaudit.googleapis.com/projects/PROJECT_ID/logs/data_access` |

The route keeps the source whole, slashes included.
`[Event("//cloudaudit.googleapis.com/projects/my-project/logs/data_access", "google.cloud.audit.log.v1.written")]`
receives the Data Access audit log events of the project `my-project`. Its route is
`EVENT ///cloudaudit.googleapis.com/projects/my-project/logs/data_access/google.cloud.audit.log.v1.written`.

Sources with slashes, dots, hyphens and underscores build and route. So do types with dots. A source
that contains `:`, such as a URL, fails the build with `HardenedException`. `CS0234` follows, for the
handler class that the generator did not write. A type that contains `:` fails the same way.
`dotnet build` reports this for `[Event("https://acme.example/orders", "OrderPlaced")]`:

```text
error HardenedException: The generator threw and produced no source: ArgumentException: The hintName 'EVENT.https://acme.example/orders/OrderPlaced.FunctionHandler.cs' contains an invalid character ':' at position 11. (Parameter 'hintName')
```

### Events that other adapters read first

The adapters for Cloud Storage, Pub/Sub topics and Firestore read their own events before the
Eventarc adapter does. When the service references one of the packages below, every event of that
kind goes to the handler attribute in the table. An `[Event]` handler for such an event never runs.

| Package | Events it reads | Handler attribute |
|---|---|---|
| `Hardened.Gcp.CloudRun.Storage` | Cloud Storage object events | `[Blob]` |
| `Hardened.Gcp.CloudRun.PubSub` | Pub/Sub topic events | `[Topic]` |
| `Hardened.Gcp.CloudRun.Firestore` | Firestore events | `[Change]` |

Without `Hardened.Gcp.CloudRun.PubSub`, a Pub/Sub topic's event reaches an `[Event]` handler for it.
The handler binds the push body. Its `message.data` is still base64. [Blobs](/gcp/blob),
[Topics](/gcp/topic) and [Changes](/gcp/change) cover those adapters.

## Request body and headers

The event's data is the request body. The event's `datacontenttype` becomes the request's
`Content-Type`. [Triggers](/guide/triggers) covers how the body binds to the handler's parameter.

Every other attribute of the event becomes a header named `ce-` followed by the attribute's name.
An extension attribute arrives the same way, such as `ce-tenant` for `tenant`.

| From the CloudEvent | Reaches the handler as |
|---|---|
| `data` | The request body |
| `datacontenttype` | `Content-Type` |
| `specversion` | `ce-specversion` |
| `id` | `ce-id` |
| `source` | `ce-source`, and the route |
| `type` | `ce-type`, and the route |
| `subject` | `ce-subject`, when the event has one |
| `time` | `ce-time`, when the event has one |
| `dataschema` | `ce-dataschema`, when the event has one |
| An extension attribute, such as `tenant` | `ce-tenant` |

The POST's other headers, such as `Authorization` and `User-Agent`, are not among the handler's
headers. Header names match without regard to case.

A request that carries `ce-specversion` and lacks `ce-id`, `ce-source` or `ce-type` answers 500. For
a missing `ce-id`, the service logs this message:

```text
Eventarc delivered a request that claims to be a CloudEvent and is not one: The CloudEvent is missing its required ce-id attribute.
```

A handler reads the headers through an `IExecutionRequest` parameter. The interface is in the
namespace `Hardened.Requests.Abstract.Execution`. A header can be missing. An event sent through
`ITriggerDelivery` under `[FunctionTesting]` alone carries none. The example reads each header with
`TryGetValue`:

```csharp
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Event("com.acme.orders", "OrderPlaced")]
    public void OnOrderPlaced(Order order, IExecutionRequest request)
    {
        request.Headers.TryGetValue("ce-id", out var eventId);
        request.Headers.TryGetValue("ce-time", out var time);

        logger.LogInformation("Event {EventId} at {Time}", eventId.ToString(), time.ToString());

        log.Record(order);
    }
}
```

### Structured mode

The adapter also reads the structured mode. A structured event is a POST whose `Content-Type` is
`application/cloudevents+json`, with the attributes and the data in one JSON object. The handler
receives the same body and headers as in binary mode. A `data_base64` member is decoded into the
body.

## Failures and redelivery

The service answers each event with a status:

| Case | The answer |
|---|---|
| The handler returns | `200`, with no body |
| The data does not bind to the handler's parameter | `400`, with a `ValidationError` body |
| The handler throws | `500`, with a `ServerError` body |
| No handler declares the event's source and type | `500`, with no body |
| The event lacks a required attribute | `500`, with no body |

Eventarc reads the status to decide whether the event was delivered. This event's data does not bind
to `Order`:

```http
POST / HTTP/1.1
Host: localhost:8080
Content-Type: application/json
ce-specversion: 1.0
ce-id: 0b5d2c1a-7a53-4c3e-9d1e-3f4b8a6c9e21
ce-source: com.acme.orders
ce-type: OrderPlaced
ce-time: 2026-09-23T12:45:07Z

{"id":"A-1","quantity":"two"}

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"order.quantity","code":"invalid","message":"The JSON value could not be converted to System.Int32."}]}
```

### Eventarc Standard triggers

A trigger delivers through a Pub/Sub subscription that Eventarc creates for it. The subscription's ID
begins with `eventarc-` and the trigger's region. Pub/Sub counts `102`, `200`, `201`, `202` and
`204` as an acknowledgement. Any other status, `400` included, makes it resend the event.

Eventarc resends after a delay that starts at 10 seconds and grows to 600 seconds. When a service
sends many negative acknowledgements, Pub/Sub can stop delivering for between 100 milliseconds and
60 seconds. Eventarc keeps an event for 24 hours. A trigger's dead-letter topic is set on its Pub/Sub
subscription. `gcloud eventarc triggers describe` names the subscription.

A trigger retries a failed event according to how it was created:

| Created with | Retries |
|---|---|
| The gcloud CLI | Yes |
| Terraform | Yes |
| The Eventarc page of the console | Yes |
| The Cloud Run page of the console | No, it makes one attempt |
| `gcloud eventarc triggers create` and `--max-retry-attempts=1` | No, it makes one attempt |

Eventarc can deliver an event more than once. Events with the same `source` and `id` are duplicates.

### Eventarc Advanced pipelines

A pipeline retries the statuses `408`, `409`, `429`, `500`, `502`, `503` and `504`. By default it
makes five attempts. The first retry comes after one second. The delay then doubles, up to 60
seconds. Any other status is a persistent error. An event whose attempts run out is also a persistent
error. The pipeline does not retry a persistent error.

An event whose data does not bind answers `400`. A trigger resends that event. A pipeline does not
retry it.

### Cloud Functions 2nd gen

A 2nd gen function whose trigger comes from `--trigger-event-filters` retries a failed event only
when `gcloud functions deploy` has `--retry`. With `--retry`, Google retries such an event for up to
7 days.

::: warning
An event whose handler fails is lost when its trigger makes one attempt. A trigger created from the
Cloud Run page of the console makes one attempt. So does a function deployed without `--retry`. A
trigger that retries discards the event after 24 hours, unless its subscription has a dead-letter
topic.
:::

Google's documentation covers retries on
[Eventarc Standard](https://docs.cloud.google.com/eventarc/docs/retry-events) and
[Eventarc Advanced](https://docs.cloud.google.com/eventarc/advanced/docs/retry-events), and
[dead-letter topics](https://docs.cloud.google.com/pubsub/docs/dead-letter-topics).

## Sending events to the service

The service deploys the same way for every trigger. The Google Cloud [Overview](/gcp/) covers it.

### An application's own events

An application publishes its own events to an Eventarc Advanced bus. An enrollment matches events on
the bus and sends them to a pipeline. The pipeline delivers them to its destination. The
destination takes these keys:

| Key | Value |
|---|---|
| `http_endpoint_uri` | The service's `run.app` URL, or a Cloud Functions function's URL |
| `google_oidc_authentication_service_account` | For a service that requires authentication, the service account whose token the pipeline sends |

That service account needs permission to invoke the service.

The enrollment's CEL expression can match the `source` and the `type` that the handler declares, as
`message.source` and `message.type`. An event that the enrollment lets through and no handler
declares answers 500.

These commands create a bus, a pipeline to the service and an enrollment for the handler's events.
`https://orders-abc123-uc.a.run.app` stands for the service's URL.

```bash
gcloud eventarc message-buses create orders --location=us-central1

gcloud eventarc pipelines create orders-service \
    --destinations=http_endpoint_uri='https://orders-abc123-uc.a.run.app',google_oidc_authentication_service_account=eventarc-invoker@my-project.iam.gserviceaccount.com \
    --location=us-central1

gcloud eventarc enrollments create order-placed \
    --cel-match="message.source == 'com.acme.orders' && message.type == 'OrderPlaced'" \
    --destination-pipeline=orders-service \
    --message-bus=orders \
    --location=us-central1
```

`gcloud eventarc message-buses publish` publishes an event:

```bash
gcloud eventarc message-buses publish orders \
    --location=us-central1 \
    --event-id=6a7e8feb-b491-4cf7-a9f1-bf3703467718 \
    --event-source=com.acme.orders \
    --event-type=OrderPlaced \
    --event-data='{"id": "A-1", "quantity": 2}' \
    --event-attributes=datacontenttype=application/json
```

The command's flags set these parts of the event:

| Flag | Sets |
|---|---|
| `--event-id` | The event's `id` |
| `--event-source` | The event's `source` |
| `--event-type` | The event's `type` |
| `--event-data` | The event's data |
| `--event-attributes=datacontenttype=application/json` | The data's content type |

The publisher keeps `source` and `id` unique for each distinct event.

### Google's events

A trigger sends the events of a Google source to the service. `gcloud eventarc triggers create`
takes the event's `type` and its other attributes in `--event-filters`. It takes the service in
`--destination-run-service`.

The command below sends BigQuery's completed jobs, which Cloud Audit Logs records, to the service
`orders`. The trigger's events carry the Cloud Audit Logs `source` from the table under Routing.

```bash
gcloud eventarc triggers create bigquery-jobs \
    --location=global \
    --destination-run-service=orders \
    --destination-run-region=us-central1 \
    --event-filters="type=google.cloud.audit.log.v1.written" \
    --event-filters="serviceName=bigquery.googleapis.com" \
    --event-filters="methodName=jobservice.jobcompleted" \
    --service-account=eventarc-invoker@my-project.iam.gserviceaccount.com
```

A trigger for Cloud Audit Logs events can be `global`. A `global` trigger receives the matching
events from every location. The trigger's service account needs the roles `roles/run.invoker` and
`roles/eventarc.eventReceiver`.

On Cloud Functions 2nd gen, `gcloud functions deploy` can create the trigger itself, from
`--trigger-event-filters`. Failures and redelivery, above, covers its `--retry`.

Google's documentation covers buses, enrollments, publishing and triggers:

- [Create an enrollment to receive events](https://docs.cloud.google.com/eventarc/advanced/docs/receive-events/create-enrollment)
- [Publish events directly](https://docs.cloud.google.com/eventarc/advanced/docs/publish-events/publish-events-direct-format)
- [Receive a Cloud Audit Logs event](https://docs.cloud.google.com/eventarc/standard/docs/run/cal)
- [gcloud eventarc triggers create](https://docs.cloud.google.com/sdk/gcloud/reference/eventarc/triggers/create)

## Testing

`[Event]` has no façade. [Testing functions](/guide/testing-functions) covers sending an event
through `ITriggerDelivery`. The test on that page passes against the handlers on this page.

In the test, the event's path is `/`, the source, `/` and the type. A Google source gives three
slashes, such as
`///cloudaudit.googleapis.com/projects/my-project/logs/data_access/google.cloud.audit.log.v1.written`.

Under `[CloudRunTesting]`, each message becomes one binary-mode CloudEvent posted to the test's
host. The event goes through the Eventarc adapter as a deployed event does. It carries these values:

| In the test's event | Value |
|---|---|
| `ce-source` | The source in the path |
| `ce-type` | The type in the path |
| `ce-id` | The type and the message's index, such as `OrderPlaced-0` |
| `ce-time` | `2026-01-01T00:00:00.000Z` |
| The data | The message as JSON |

Under `[CloudRunTesting]` and under `[FunctionTesting]` alone, a handler that throws makes the test's
call throw the handler's exception.

The Google Cloud [Testing](/gcp/testing) page covers `[CloudRunTesting]` itself.

## Next

- [Triggers](/guide/triggers): the trigger attributes and payload binding
- [Topics](/gcp/topic): Pub/Sub topics, whose events the Pub/Sub adapter reads
- [Blobs](/gcp/blob): Cloud Storage events, which the Storage adapter reads
- [Overview](/gcp/): the packages, the application class, the entry point, running locally and
  deploying
- [Testing functions](/guide/testing-functions): testing a trigger handler

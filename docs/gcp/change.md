# Changes

`[Change("orders")]` on a method makes it the handler for changes to the documents in the Firestore
collection `orders`. Eventarc delivers each change to the service as one event in one request, and
the handler runs once for it.

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Change("orders")]
    public void OnOrderChanged(Order order) => log.Record(order);
}
```

The application class names only the host:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Gcp.CloudRun.Runtime;

namespace Orders;

[HardenedModule]
[CloudRunRuntime]
public partial class Application;
```

The `hardened-function` template writes both files. The template's `Order` has a string `Id` and an
int `Quantity`. Its `OrderLog` is a `[SingletonService]` that keeps the orders it is given.

A Firestore event carries the document with a type on each value, such as
`"quantity": {"integerValue": "2"}`. The handler's parameter binds the document without the types,
so `Order` gets `Id` `A-1` and `Quantity` 2.

The project's reference to the adapter package, `Hardened.Gcp.CloudRun.Firestore`, makes `[Change]`
mean Firestore. The build registers the package's `FirestoreModule` on the application. The module
has no settings. An application does not need to declare it.

The same application serves the same events as a Cloud Functions 2nd gen function. The handler and
the adapter do not change.

[Triggers](/guide/triggers) covers `[Change]` and compares the change feeds of the three clouds.

## Packages

This command writes the service and a test project:

```bash
dotnet new hardened-function -n Orders --host gcp --trigger change
```

A change service references `Hardened.Gcp.CloudRun.Firestore` beside
`Hardened.Gcp.CloudRun.Runtime`:

```xml
<PackageReference Include="Hardened.Gcp.CloudRun.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Gcp.CloudRun.Firestore" Version="0.0.0-HARDENED-VERSION" />
```

On Cloud Functions 2nd gen, `Hardened.Gcp.Functions.Runtime` takes the place of
`Hardened.Gcp.CloudRun.Runtime`. `Hardened.Gcp.CloudRun.Firestore` stays. The Google Cloud
[Overview](/gcp/) covers the host packages, the entry point and the project settings of both hosts.

`Hardened.Gcp.CloudRun.Firestore` brings `Google.Events.Protobuf` 1.8.0, which defines
`DocumentEventData` and `Document`. A handler that uses those types needs no package reference of
its own.

## Routing

The adapter takes the collection from the event's `ce-subject`, which is `documents/` followed by
the document's path. An event with `ce-subject: documents/orders/A-1` reaches `[Change("orders")]`
under the route `CHANGE /orders`.

The route is the collection the document is in. A document in a subcollection routes on its own
collection, so `documents/users/u-1/orders/A-7` reaches `[Change("orders")]` too. The name in
`[Change]` matches the collection exactly, including case.

The service answers 500 to an event from a collection that no handler names. It logs an
`InvalidOperationException` with this message:

```text
No handler is registered for CHANGE /payments. An event source is wired to this function that no trigger attribute declared.
```

An event without `ce-subject` routes to `CHANGE /`. No handler declares that route, so the service
answers 500.

The database is not part of the route. An event from another database of the project reaches the
same handler. Its `ce-source` and `ce-database` headers name the database.

## Event types and path patterns

The handler receives each of the four event types that Firestore raises:

| Event type | Raised when |
|---|---|
| `google.cloud.firestore.document.v1.created` | A document is written for the first time |
| `google.cloud.firestore.document.v1.updated` | A document that exists has a value changed |
| `google.cloud.firestore.document.v1.deleted` | A document with data is deleted |
| `google.cloud.firestore.document.v1.written` | A document is created, updated or deleted |

Each type has a form that ends in `.withAuthContext`, which adds the `authtype` and `authid`
attributes. The handler receives those forms too.

The trigger's event type decides which changes reach the service. A trigger on `written` sends all
three kinds of change.

A trigger watches documents and not a collection. Its path pattern names them, such as `orders/{id}`
for every document in `orders`. That pattern leaves out the documents of subcollections. A
multi-segment wildcard, such as `orders/{id=**}`, takes them in. Each of their events routes on its
own collection.

One service can serve several collections, with a handler and a trigger for each.

## The request body

The request body is the document after the change, as JSON without Firestore's value types. A
`deleted` event has no document after the change, so the body is the document as it was before it.
The section on the document before the change has a table of the body for each event type.

Each Firestore value becomes JSON in the body:

| Firestore value | In the request body |
|---|---|
| `stringValue` | A JSON string |
| `integerValue` | A JSON number, with every digit |
| `doubleValue` | A JSON number. `2.0` is written `2` |
| `booleanValue` | `true` or `false` |
| `nullValue` | `null` |
| `timestampValue` | A string in the form `2026-09-23T12:45:07.1230000+00:00` |
| `bytesValue` | A base64 string |
| `referenceValue` | The referenced document's full name, as a string |
| `geoPointValue` | An object with `latitude` and `longitude` |
| `arrayValue` | A JSON array, each member converted the same way |
| `mapValue` | A JSON object, each value converted the same way |

An `integerValue` does not bind to a `string` property. The service answers 400 with this message:

```text
The JSON value could not be converted to System.String.
```

[Triggers](/guide/triggers) covers how the body binds to the handler's parameter.

## Headers

Every attribute of the event becomes a header named `ce-` followed by the attribute's name. The
adapter adds two headers for the document.

| From the Firestore event | Reaches the handler as |
|---|---|
| `type` | `ce-type`, such as `google.cloud.firestore.document.v1.updated` |
| `subject` | `ce-subject`, such as `documents/orders/A-1`, and the route |
| The document's path | `x-goog-firestore-document`, such as `orders/A-1` |
| The document's full name | `x-goog-firestore-document-name`, such as `projects/my-project/databases/(default)/documents/orders/A-1` |
| `source` | `ce-source`, such as `//firestore.googleapis.com/projects/my-project/databases/(default)` |
| `id`, `specversion`, `time` | `ce-id`, `ce-specversion`, `ce-time` |
| The extension attributes `location`, `project`, `database`, `namespace` and `document` | `ce-location`, `ce-project`, `ce-database`, `ce-namespace`, `ce-document` |
| `authtype` and `authid`, on a `.withAuthContext` type | `ce-authtype`, `ce-authid` |
| The data | The request body, without its value types |

The request has no `Content-Type` header. Header names match without regard to case.

A handler reads the headers through an `IExecutionRequest` parameter, from the namespace
`Hardened.Requests.Abstract.Execution`. Under `[FunctionTesting]` alone, without
`[CloudRunTesting]`, the request has no headers, so this handler reads each one with `TryGetValue`:

```csharp
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Change("orders")]
    public void OnOrderChanged(Order order, IExecutionRequest request)
    {
        request.Headers.TryGetValue("ce-type", out var type);
        request.Headers.TryGetValue("x-goog-firestore-document", out var document);

        logger.LogInformation("{Type} of {Document}", type.ToString(), document.ToString());

        log.Record(order);
    }
}
```

## The whole event

Under `[Change]` the request is a `FirestoreChange`, from the namespace
`Hardened.Gcp.CloudRun.Firestore`. Its `Event` property is the whole `DocumentEventData`, with
`Value`, `OldValue` and `UpdateMask`. The request also has `Value` and `OldValue` properties of its
own.

`UpdateMask` lists the fields that an `updated` event changed. It is null on a `created` or a
`deleted` event.

Under `[FunctionTesting]` alone the request is not a `FirestoreChange`. This handler logs the fields
that an update changed:

```csharp
using Hardened.Functions.Runtime.Attributes;
using Hardened.Gcp.CloudRun.Firestore;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Change("orders")]
    public void OnOrderChanged(Order order, IExecutionRequest request)
    {
        if (request is FirestoreChange { Event.UpdateMask: { } mask })
        {
            logger.LogInformation(
                "Order {Id} changed {Fields}",
                order.Id,
                string.Join(", ", mask.FieldPaths)
            );
        }

        log.Record(order);
    }
}
```

## The document before the change

`[OldValue]` on a parameter binds the document as it was before the change. `[OldValue]` is in the
namespace `Hardened.Gcp.CloudRun.Firestore`. This handler compares the document with the one before
it:

```csharp
using Hardened.Functions.Runtime.Attributes;
using Hardened.Gcp.CloudRun.Firestore;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Change("orders")]
    public void OnOrderChanged(Order order, [OldValue] Order? previous)
    {
        if (previous != null && previous.Quantity != order.Quantity)
        {
            logger.LogInformation(
                "Order {Id} quantity changed from {Before} to {After}",
                order.Id,
                previous.Quantity,
                order.Quantity
            );
        }

        log.Record(order);
    }
}
```

On a parameter of the body's type, or of any other type that the document binds to, `[OldValue]`
binds the way the body binds. On a `Document` parameter it binds Firestore's own form, with the
value types on. The type is in the namespace `Google.Events.Protobuf.Cloud.Firestore.V1`.

The body and `[OldValue]` bind these documents for each event type:

| Event type | The body | `[OldValue]` |
|---|---|---|
| `created` | The document after the change | Null |
| `updated` | The document after the change | The document before the change |
| `deleted` | The document before the change | The document before the change |
| `written` | As `created`, `updated` or `deleted`, by the change it reports | As `created`, `updated` or `deleted`, by the change it reports |

A parameter declared without `?`, such as `[OldValue] Order previous`, is also null on a `created`
event. `[OldValue]` raises no error.

`[OldValue]` reads the Firestore event off the request. When the request carries no Firestore event,
the handler fails with an `InvalidOperationException`:

```text
[OldValue] was bound on a handler that is not serving a Firestore change. It reads the document event off the request, so it only works under [Change].
```

## What the service answers

The service answers each event with a status. Eventarc reads the status to decide whether the event
was delivered.

| Case | The answer |
|---|---|
| The handler returns | `200`, with no body |
| The document does not bind to the handler's parameter | `400`, with a `ValidationError` body |
| The handler throws | `500`, with a `ServerError` body |
| No handler names the collection | `500`, with no body |
| The event's data is not protobuf | `500`, with no body |

## Redelivery

A trigger delivers through a Pub/Sub subscription that Eventarc creates for it. The subscription's
ID begins with `eventarc-` and the trigger's location. Pub/Sub counts `102`, `200`, `201`, `202` and
`204` as an acknowledgement. Any other status, `400` included, makes it resend the event.

Eventarc resends after a delay that starts at 10 seconds and grows to 600 seconds. It keeps an event
for 24 hours. An event that is not delivered within the 24 hours is discarded, unless the trigger's
subscription has a dead-letter topic. The dead-letter topic is set on that Pub/Sub subscription,
which `gcloud eventarc triggers describe` names.

A trigger retries a failed event according to how it was created:

| Trigger created | Retries a failed event |
|---|---|
| With `gcloud eventarc triggers create` | Yes |
| With `gcloud eventarc triggers create` and `--max-retry-attempts=1` | No. It makes one attempt |
| With Terraform | Yes |
| From the Eventarc page of the console | Yes |
| From the Cloud Run page of the console | No. It makes one attempt |
| With `gcloud functions deploy --trigger-event-filters`, for a 2nd gen function | Only when the command has `--retry`, and then for up to 7 days |

::: warning
A change whose handler fails is lost when its trigger makes one attempt. A trigger created from the
Cloud Run page of the console makes one attempt. So does the trigger of a function deployed without
`--retry`. A trigger that retries discards the change after 24 hours, unless its subscription has a
dead-letter topic.
:::

When a service sends many negative acknowledgements, Pub/Sub can stop delivering for between 100
milliseconds and 60 seconds.

Firestore does not guarantee the order of its events. Rapid changes can arrive in an unexpected
order. An event can arrive more than once. Events with the same `source` and `id` are duplicates.
The headers `ce-source` and `ce-id` carry those two attributes.

Google's documentation covers [retries](https://docs.cloud.google.com/eventarc/docs/retry-events)
and [dead-letter topics](https://docs.cloud.google.com/pubsub/docs/dead-letter-topics).

## Creating the trigger

An Eventarc trigger sends a database's document events to the service. The template's README gives
this `gcloud eventarc triggers create` command, here with the service and the service account filled
in:

```bash
gcloud eventarc triggers create orders \
    --location=nam5 \
    --destination-run-service=orders \
    --destination-run-region=us-central1 \
    --event-filters="type=google.cloud.firestore.document.v1.written" \
    --event-filters="database=(default)" \
    --event-filters-path-pattern="document=orders/{id}" \
    --event-data-content-type="application/protobuf" \
    --service-account=eventarc-invoker@my-project.iam.gserviceaccount.com
```

| Option | Takes |
|---|---|
| `--location` | The trigger's location. It is the database's location, such as `nam5` for a database in that multi-region |
| `--destination-run-service` | The service. On Cloud Functions 2nd gen, the function |
| `--destination-run-region` | The service's region, when it differs from the trigger's location |
| `--event-filters` with `type` | The event type. It cannot be changed after the trigger is created |
| `--event-filters` with `database` | The one database that the trigger watches. `(default)` is the default database |
| `--event-filters-path-pattern` | The documents' path pattern. Its collection is the name in `[Change]` |
| `--event-data-content-type` | `application/protobuf`. Without it, Eventarc sends the data as JSON, which is the default |
| `--service-account` | The trigger's service account. It needs `roles/run.invoker` and `roles/eventarc.eventReceiver` |

The adapter reads only protobuf data. With JSON data the service answers 500 to every event. It logs
an `InvalidOperationException` with this message:

```text
The Firestore delivery carries data that is not a DocumentEventData.
```

The service deploys the same way for every trigger. The Google Cloud [Overview](/gcp/) covers
deploying it.

Google's documentation covers Firestore triggers in
[Trigger functions with Firestore documents](https://docs.cloud.google.com/run/docs/triggering/trigger-functions-with-firestore-documents)
and
[Extend Firestore with event triggers using Cloud Run functions](https://docs.cloud.google.com/firestore/native/docs/extend-with-cloud-run-functions).
[Eventarc locations](https://docs.cloud.google.com/eventarc/docs/locations) covers their locations.

## Testing

`dotnet new hardened-function --host gcp --trigger change` writes this test. It sends a change
through `Application.Changes`.

```csharp
using DependencyModules.xUnit.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderHandlerTests
{
    [ModuleTest]
    public async Task AChangedRowReachesTheHandler(Application.Changes changes, OrderLog log)
    {
        await changes.Orders(new Order { Id = "A-1", Quantity = 2 });

        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

The template's test project declares `[assembly: CloudRunTesting]` and `[assembly: WebTesting]`.
They post each message of a façade call to the test's host as one Firestore event, so the change
goes through the Firestore adapter.

Each test event is an `updated` event with `application/protobuf` data. The document after the
change and the document before it are both the message. The document's ID is the message's `id`
property. A message without one gets `document-` followed by its index. The collection is the one
that the façade method is named for.

The message's values become these Firestore values:

| In the message | In the test event |
|---|---|
| A number with no fraction | `integerValue` |
| A number with a fraction | `doubleValue` |
| A string | `stringValue` |
| `true` or `false` | `booleanValue` |
| An object | `mapValue`, each value converted the same way |
| An array | `arrayValue`, each member converted the same way |
| `null` | `nullValue` |

The template's test sends an event with these headers:

| Header | Value |
|---|---|
| `ce-id` | `orders-0` |
| `ce-source` | `//firestore.googleapis.com/projects/test-project/databases/(default)` |
| `ce-subject` | `documents/orders/A-1` |
| `ce-time` | `2026-01-01T00:00:00.000Z` |
| `ce-type` | `google.cloud.firestore.document.v1.updated` |
| `x-goog-firestore-document` | `orders/A-1` |
| `x-goog-firestore-document-name` | `projects/test-project/databases/(default)/documents/orders/A-1` |

Under `[FunctionTesting]` alone the request has no headers and is not a `FirestoreChange`. A handler
that binds `[OldValue]` fails there with the `InvalidOperationException` above. The handler that
reads headers and the handler that reads `UpdateMask` pass the template's test under
`[CloudRunTesting]` and under `[FunctionTesting]` alone. The handler that compares the document with
the one before it passes under `[CloudRunTesting]` only.

A handler whose only parameter is `[OldValue]` gets a façade method with no parameters. Under both
deliveries, a call to it sends nothing and the handler does not run.

[Testing functions](/guide/testing-functions) covers what a failed message does to the façade call
under each delivery. The Google Cloud [Testing](/gcp/testing) page covers `[CloudRunTesting]`
itself.

## Next

- [Triggers](/guide/triggers): the trigger attributes, and the change feeds of the three clouds
- [Events](/gcp/event): other Eventarc events, with `[Event]`
- [Overview](/gcp/): the packages, the application class, the entry point, running locally and
  deploying
- [Testing functions](/guide/testing-functions): testing a trigger handler
- [Testing](/gcp/testing): testing on Google Cloud

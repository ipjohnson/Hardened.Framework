# Events

`[Event("com.acme.orders", "OrderPlaced")]` on a method makes it the handler for Event Grid events
whose `source` is `com.acme.orders` and whose `type` is `OrderPlaced`. The handler's parameter binds
the event's data.

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

The application class names no module. This is `src/Orders/Application.cs` as the template writes
it for `--host azure`:

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
public partial class Application;
```

The reference to `Hardened.Azure.Functions.EventGrid` makes `[Event]` mean Event Grid. The build
registers the package's `EventGridModule` on the application. It also writes the `Event` function.

Event Grid delivers each event to the function app's `Event` function as a CloudEvent, when the
subscription delivers in the CloudEvents schema. Each event is one invocation of `Event`. The
handler runs once for each event. [Triggers](/guide/triggers) covers `[Event]` and its two
arguments.

## Packages

An event function app references `Hardened.Azure.Functions.EventGrid` beside
`Hardened.Azure.Functions.Runtime`. In a project without central package management, the two
references are:

```xml
<PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Azure.Functions.EventGrid" Version="0.0.0-HARDENED-VERSION" />
```

The other packages, among them `Microsoft.Azure.Functions.Worker.Sdk` and
`Hardened.Azure.Functions.SourceGenerator`, are the same for every Azure function app. So are
`Program.cs`, `host.json` and the project settings. The Azure [Overview](/azure/) covers them.

`dotnet new hardened-function` has no `--trigger` value for events. The app on this page starts from
`dotnet new hardened-function -n Orders --host azure --trigger queue`. Its `src/Orders/Orders.csproj`
references `Hardened.Azure.Functions.EventGrid` in place of `Hardened.Azure.Functions.ServiceBus`.

The scaffold manages package versions centrally. Its `Directory.Packages.props` has no line for
`Hardened.Azure.Functions.EventGrid`, so the package reference alone fails the restore with
`NU1010`:

```text
The PackageReference items Hardened.Azure.Functions.EventGrid do not have corresponding PackageVersion.
```

This line in `Directory.Packages.props`, beside the other `Hardened.Azure.Functions` lines, fixes
the restore:

```xml
<PackageVersion Include="Hardened.Azure.Functions.EventGrid" Version="$(HardenedVersion)" />
```

## Routing

The `Event` function is the one function for all of the app's `[Event]` handlers. One app can serve
several events, with a handler for each. The function's trigger is an Event Grid trigger that binds
each event as a string.

The adapter in `Hardened.Azure.Functions.EventGrid` reads the event's `source` and `type`. It routes
the event as `EVENT /{source}/{type}`. An event with `source` `com.acme.orders` and `type`
`OrderPlaced` reaches `[Event("com.acme.orders", "OrderPlaced")]`, under the route
`EVENT /com.acme.orders/OrderPlaced`. The source and the type match the attribute's arguments
exactly, including case.

An event that no handler declares fails the invocation with `InvalidOperationException`:

```text
No handler is registered for EVENT /com.acme.orders/OrderShipped. An event source is wired to this function that no trigger attribute declared.
```

The adapter reads only the CloudEvents schema, so the subscription must deliver in it, as
[Subscribing to a custom topic](#subscribing-to-a-custom-topic) shows. An event in the Event Grid
schema fails the invocation with `CloudEventFormatException`:

```text
The CloudEvent is missing its required specversion attribute.
```

A source or a type that contains `:`, such as `urn:acme:orders`, fails the build with
`HardenedException`. `CS0234` follows, for the handler class that the generator did not write. For
`[Event("urn:acme:orders", "OrderPlaced")]`, `dotnet build` reports:

```text
error HardenedException: The generator threw and produced no source: ArgumentException: The hintName 'EVENT.urn:acme:orders/OrderPlaced.FunctionHandler.cs' contains an invalid character ':' at position 9. (Parameter 'hintName')
```

### Events from Azure services

An Azure service's event in the CloudEvents schema has the resource's ID as its `source`.
For Blob Storage,
[CloudEvents Integration with Azure Event Grid](https://learn.microsoft.com/en-us/azure/event-grid/cloud-event-schema)
gives the `source` as
`/subscriptions/{subscription-id}/resourceGroups/{resource-group}/providers/Microsoft.Storage/storageAccounts/{storage-account}`,
with a `type` such as `Microsoft.Storage.BlobCreated`. The route keeps the source whole, leading
slash included.

The handler in `src/Orders/UploadHandler.cs` receives the `Microsoft.Storage.BlobCreated` events of
the storage account `ordersstorage`. Its route is
`EVENT //subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/orders/providers/Microsoft.Storage/storageAccounts/ordersstorage/Microsoft.Storage.BlobCreated`.
Its parameter, a `BlobCreated` from `src/Orders/BlobCreated.cs`, binds the event's `data`.

```csharp
namespace Orders;

public class BlobCreated
{
    public string Url { get; set; } = "";

    public long ContentLength { get; set; }
}
```

```csharp
using Hardened.Functions.Runtime.Attributes;
using Microsoft.Extensions.Logging;

namespace Orders;

public class UploadHandler(ILogger<UploadHandler> logger)
{
    [Event(
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/orders/providers/Microsoft.Storage/storageAccounts/ordersstorage",
        "Microsoft.Storage.BlobCreated"
    )]
    public void OnBlobCreated(BlobCreated created)
    {
        logger.LogInformation("Blob {Url}, {Length} bytes", created.Url, created.ContentLength);
    }
}
```

[Blobs](/azure/blob) covers Blob Storage through `[Blob]`, which the build serves with a blob trigger
of its own.

## Body and headers

The event's `data` is the request body. Its attributes become headers:

| From the CloudEvent | Reaches the handler as |
|---|---|
| `data`, or `data_base64` decoded | The request body |
| `datacontenttype` | `Content-Type`, `application/json` when the event has none |
| `specversion` | `ce-specversion` |
| `id` | `ce-id` |
| `source` | `ce-source`, and the route |
| `type` | `ce-type`, and the route |
| `subject` | `ce-subject`, when the event has one |
| `time` | `ce-time`, when the event has one |
| `dataschema` | `ce-dataschema`, when the event has one |
| An extension attribute, such as `tenant` | `ce-tenant` |

JSON data arrives as compact JSON. The adapter decodes a `data_base64` member into the body. A
`data` string whose `datacontenttype` is not JSON becomes the body's text. An event without data has
an empty body. [Triggers](/guide/triggers) covers how the body binds to the handler's parameter.

Every attribute except `datacontenttype` becomes a header named `ce-` followed by the attribute's
name. An extension attribute arrives the same way. An attribute whose value is a number arrives as
its text. The POST's own headers, such as `aeg-event-type`, do not reach the handler. Header names
match without regard to case.

A handler reads the headers through an `IExecutionRequest` parameter, from
`Hardened.Requests.Abstract.Execution`. This `OrderHandler` reads two of them:

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

The handler reads each header with `TryGetValue`, because a header can be missing. An event sent
through `ITriggerDelivery` under `[FunctionTesting]` alone carries none.

## The Event Grid webhook

Under `func start`, an event posted to the host's Event Grid webhook runs the handler above:

```http
POST /runtime/webhooks/eventgrid?functionName=Event HTTP/1.1
Host: localhost:7071
aeg-event-type: Notification
Content-Type: application/cloudevents+json; charset=utf-8

{"specversion":"1.0","id":"6a7e8feb-b491-4cf7-a9f1-bf3703467718","source":"com.acme.orders","type":"OrderPlaced","time":"2026-09-24T12:45:07Z","datacontenttype":"application/json","data":{"id":"A-1","quantity":2}}

HTTP/1.1 202 Accepted
```

Core Tools serve the Event Grid webhook at `/runtime/webhooks/eventgrid`, with the function's name
in `functionName`. A POST needs the header `aeg-event-type: Notification`. Without it, the host
answers 400. For a `functionName` that no function has, the host answers 404 with
`cannot find function: 'Events'`.

Event Grid sends an event in the CloudEvents schema with
`Content-Type: application/cloudevents+json; charset=utf-8`. The host reads one CloudEvent or an
array of them, whatever the POST's `Content-Type`.

Under `func start`, the webhook needs no key. In Azure, the webhook's URL carries the
`eventgrid_extension` system key in `code`.
[How to work with Event Grid triggers and bindings in Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/event-grid-how-tos)
covers the webhook in Azure. The Azure [Overview](/azure/) covers running the app locally.

## Failures

The host answers Event Grid's request with 202 when the event ran, and with 500 when its invocation
failed:

| Case | The invocation | The host answers |
|---|---|---|
| The handler returns | Succeeds | `202 Accepted`, with no body |
| The handler throws | Fails with the handler's exception | `500 Internal Server Error`, with the body `Exception while executing function: Functions.Event` |
| The data does not bind to the handler's parameter | Fails with `JsonException` | `500`, with the same body |
| No handler declares the event's `source` and `type` | Fails with `InvalidOperationException` | `500`, with the same body |
| The event is not a CloudEvent, such as an event in the Event Grid schema | Fails with `CloudEventFormatException` | `500`, with the same body |

A request that carries several events runs one invocation for each, as
[Azure Event Grid trigger for Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-event-grid-trigger)
describes. When any of them fails, the host answers 500 for the whole request.

## Redelivery

Event Grid counts 200 to 204 as a delivered event. Every other status is a failed delivery. Event
Grid waits 30 seconds for an answer before it counts the attempt as failed.

Event Grid retries a failed delivery after about 10 seconds, 30 seconds, 1 minute, 5 minutes, 10
minutes, 30 minutes, 1 hour, 3 hours and 6 hours, then every 12 hours, up to 24 hours. By default,
Event Grid stops after 30 attempts or 1,440 minutes, whichever comes first. Event Grid drops an event
whose retries run out, unless the subscription names a dead-letter container. Dead-lettering is off
by default.

::: warning
A subscription without a dead-letter container drops an event whose handler keeps failing, after 30
attempts or 24 hours. The event is lost.
:::

Event Grid delivers each event at least once, so an event can arrive more than once. A subscription
can batch events. Event Grid delivers and retries a batch as a whole, so a 500 for one event sends
every event of the batch again. Batching is off by default. After a run of failures, Event Grid
delays new deliveries to the endpoint, in some cases for several hours.
[Azure Event Grid Delivery and Retry Explained](https://learn.microsoft.com/en-us/azure/event-grid/delivery-and-retry)
covers the retry schedule, the retry policy, dead-lettering and batching.

## Settings

`EventGridModule` has no settings. An application does not need to declare it. The `Event`
function's binding has no settings either, so the subscription alone decides what reaches the
function.

## Subscribing to a custom topic

An application publishes its own events to an Event Grid custom topic. A topic created with
`--input-schema cloudeventschemav1_0` takes CloudEvents. A publisher posts events to the topic's
endpoint with one of the topic's access keys in the `aeg-sas-key` header, as
[Post Events to Azure Event Grid Custom Topics](https://learn.microsoft.com/en-us/azure/event-grid/post-to-custom-topic)
describes.

An event subscription on the topic delivers the matching events to the function. These commands
create a topic and a subscription that sends the topic's `OrderPlaced` events to the function.
`orders` is the function app, the topic and the resource group. `ordersstorage` is a storage account with a
`deadletters` container.

```bash
az eventgrid topic create --name orders --resource-group orders --location eastus \
    --input-schema cloudeventschemav1_0

az eventgrid event-subscription create --name orders-function \
    --source-resource-id "$(az eventgrid topic show --name orders --resource-group orders --query id --output tsv)" \
    --endpoint "$(az functionapp show --name orders --resource-group orders --query id --output tsv)/functions/Event" \
    --endpoint-type azurefunction \
    --event-delivery-schema cloudeventschemav1_0 \
    --included-event-types OrderPlaced \
    --deadletter-endpoint "$(az storage account show --name ordersstorage --resource-group orders --query id --output tsv)/blobServices/default/containers/deadletters"
```

The first command sends
`PUT /subscriptions/{subscription}/resourceGroups/orders/providers/Microsoft.EventGrid/topics/orders`,
with `"location": "eastus"` and `"properties": {"inputSchema": "CloudEventSchemaV1_0", ...}`. The
second sends `PUT .../topics/orders/providers/Microsoft.EventGrid/eventSubscriptions/orders-function`.
Its options set the subscription's destination, delivery schema, filter and dead-letter destination:

| Option | Effect |
|---|---|
| `--endpoint-type azurefunction` | `--endpoint` is the function's resource ID: the function app's ID followed by `/functions/Event`. Event Grid validates the endpoint and fetches the function's key itself |
| `--event-delivery-schema cloudeventschemav1_0` | The subscription delivers CloudEvents, which the adapter needs. Without it, a subscription delivers in its topic's input schema |
| `--included-event-types` | The subscription lets through only the listed types. They are the second argument of each handler's `[Event]` |
| `--deadletter-endpoint` | The named blob container receives events whose retries run out |

The CLI also sends the default retry policy, `"maxDeliveryAttempts": 30` and
`"eventTimeToLiveInMinutes": 1440`. `--max-delivery-attempts` and `--event-ttl` change it.
[az eventgrid event-subscription](https://learn.microsoft.com/en-us/cli/azure/eventgrid/event-subscription)
describes each option.
[Event Grid Event Handler with Azure Functions](https://learn.microsoft.com/en-us/azure/event-grid/handler-functions)
describes the Azure Function endpoint type.

An event that the subscription lets through and no handler declares fails the invocation. A topic in
the Event Grid schema can deliver in the CloudEvents schema. A topic in the CloudEvents schema cannot
deliver in the Event Grid schema.

A subscription can also use the webhook endpoint type, with the URL
`https://{functionappname}.azurewebsites.net/runtime/webhooks/eventgrid?functionName=Event&code={systemkey}`.
The host answers Event Grid's validation of the endpoint itself.

## Subscribing to an Azure service

A subscription whose `--source-resource-id` is an Azure resource, such as a storage account,
delivers that service's events. In the CloudEvents schema, their `source` is the resource's ID,
which the handler's `[Event]` names, as [Events from Azure services](#events-from-azure-services)
shows. This command subscribes the function to a storage account's `Microsoft.Storage.BlobCreated`
events:

```bash
az eventgrid event-subscription create --name uploads-function \
    --source-resource-id "$(az storage account show --name ordersstorage --resource-group orders --query id --output tsv)" \
    --endpoint "$(az functionapp show --name orders --resource-group orders --query id --output tsv)/functions/Event" \
    --endpoint-type azurefunction \
    --event-delivery-schema cloudeventschemav1_0 \
    --included-event-types Microsoft.Storage.BlobCreated
```

Deploying the function app itself is the same for every trigger. The Azure [Overview](/azure/)
covers it.

## Testing

`[Event]` has no façade. [Testing functions](/guide/testing-functions) covers sending an event
through `ITriggerDelivery`. Its event test passes against the handlers on this page.

Under `[AzureFunctionsTesting]`, each message becomes one structured CloudEvent, handed to the
`Event` function's invocation handler:

| In the CloudEvent | Value |
|---|---|
| `specversion` | `1.0` |
| `id` | A new GUID |
| `source` | The path passed to `Deliver`, up to its last `/` |
| `type` | The rest of the path |
| `time` | The current time |
| `datacontenttype` | `application/json` |
| `data` | The message as JSON |

A source that begins with `/`, as an Azure resource ID does, does not reach its handler under
`[AzureFunctionsTesting]`. The delivery drops that `/`. The call then fails:

```text
No handler is registered for EVENT /subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/orders/...
```

Under `[FunctionTesting]` alone, the event reaches the handler. When the handler throws, the call
throws the handler's exception under both. The Azure [Testing](/azure/testing) page covers
`[AzureFunctionsTesting]`.

## Next

- [Triggers](/guide/triggers): the trigger attributes and payload binding
- [Blobs](/azure/blob): Blob Storage, through `[Blob]` and its own trigger
- [Topics](/azure/topic): Service Bus topics
- [Overview](/azure/): the packages, the application class, the entry point, running locally and
  deploying
- [Testing functions](/guide/testing-functions): testing a trigger handler

# Streams

`[Stream("orders")]` on a method makes it the handler for events sent to the Event Hubs event hub
named `orders`. The handler runs once for each event, in the order of the event's partition.

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Stream("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

The application class names no module:

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
public partial class Application;
```

A reference to `Hardened.Azure.Functions.EventHubs` makes `[Stream]` mean Event Hubs. The build
registers the package's `EventHubsModule` on the application. The build also writes an Azure
function for each stream handler. The Functions host invokes that function with a batch of events
from one partition.

The handler's parameter binds the event's body. In the template, `Order` has a string `Id` and an
int `Quantity`. `OrderLog` is a `[SingletonService]` that keeps the orders it is given.
[Triggers](/guide/triggers) covers `[Stream]` and the other trigger attributes.

## Packages

This command writes the function app and a test project:

```bash
dotnet new hardened-function -n Orders --host azure --trigger stream
```

A stream function app references the Event Hubs package beside `Hardened.Azure.Functions.Runtime`:

```xml
<PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Azure.Functions.EventHubs" Version="0.0.0-HARDENED-VERSION" />
```

The Event Hubs package brings the worker's Event Hubs extension,
`Microsoft.Azure.Functions.Worker.Extensions.EventHubs` 6.5.0. The build adds the host's Event Hubs
extension 6.5.2 to the function app.

The Worker SDK, the generator package, `Program.cs` and the project settings are the same for every
Azure function app. The Azure [Overview](/azure/) covers them.

## Functions and routes

The build names each function for its hub. `[Stream("orders")]` gives `Stream_orders`. The Azure
[Overview](/azure/) covers how functions are named.

The function carries the Event Hubs extension's trigger on the handler's hub. It receives the events
in batches. It names the app setting that holds the connection. The build describes this binding to
the host in `src/Orders/bin/Debug/net8.0/functions.metadata`:

```json
{
  "name": "events",
  "direction": "In",
  "type": "eventHubTrigger",
  "eventHubName": "orders",
  "connection": "AzureWebJobsEventHubs",
  "cardinality": "Many",
  "properties": {
    "supportsDeferredBinding": "True"
  }
}
```

`func start` lists the function as `Stream_orders: eventHubTrigger`.

The host binds each function to one hub, so the route comes from the function. Every event that
`Stream_orders` receives goes to the `orders` handler, under the route `STREAM /orders`. A hub name
in the connection string takes the place of the name in `[Stream]`. The route stays the handler's,
as the warning under [Deploying](#deploying) shows.

One function app can serve several hubs of one namespace, with one handler, and so one function,
for each hub. Every stream handler of an application reads through the one connection and the one
consumer group that the Event Hubs module names. [Module settings](#module-settings) lists both.

## Body and headers

The request body is the event's body: the bytes the publisher sent. The handler's parameter type
decides how the body binds. A JSON object binds a class such as `Order`. A `byte[]` parameter
receives the body as the publisher wrote it, JSON or not. [Triggers](/guide/triggers) covers how the
body binds to the handler's parameter.

An event with an empty body fails its item when the handler's parameter cannot be null.

| Parameter | An event with an empty body |
|---|---|
| A class, such as `Order` | Fails with `JsonException`: `The input does not contain any JSON tokens. Expected the input to start with a valid JSON token, when isFinalBlock is true.` |
| `byte[]` | Fails with `ValidationException`: `One or more validation errors occurred.` |
| `byte[]?` | Binds null |

Each of the event's application properties becomes a header with the property's name. The Event
Hubs adapter adds headers for the content type and for what the hub recorded about the event:

| From the event | Reaches the handler as |
|---|---|
| The body | The request body |
| The content type | `Content-Type`, when the publisher set one |
| Each application property | A header with the property's name |
| The sequence number | `x-azure-eventhubs-sequence-number`, the event's number within its partition |
| The offset | `x-azure-eventhubs-offset`, the event's position in its partition, as the service wrote it |
| The partition key | `x-azure-eventhubs-partition-key`, when the publisher set one |
| The enqueued time | `x-azure-eventhubs-enqueued-time`, when the hub accepted the event, in ISO 8601 |
| The message id | `x-azure-eventhubs-message-id`, when the publisher set one |
| The correlation id and the other system properties | Nothing |

A property's value arrives as text:

| Property value | Header value |
|---|---|
| A number or a GUID | The text that the invariant culture writes |
| `true` | `True` |
| A time | ISO 8601, in UTC |
| A byte array | No header |

The adapter writes its own headers after the properties. Its header replaces a property with the
same name. Header names are matched without regard to case.

A handler reads the headers through an `IExecutionRequest` parameter. The interface is in namespace
`Hardened.Requests.Abstract.Execution`. This version of `src/Orders/OrderHandler.cs` logs two of
them:

```csharp
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Stream("orders")]
    public void OnOrder(Order order, IExecutionRequest request)
    {
        request.Headers.TryGetValue("x-azure-eventhubs-partition-key", out var partitionKey);
        request.Headers.TryGetValue("x-azure-eventhubs-sequence-number", out var sequenceNumber);

        logger.LogInformation(
            "Order {Id} with partition key {PartitionKey} at sequence number {SequenceNumber}",
            order.Id,
            partitionKey.ToString(),
            sequenceNumber.ToString()
        );

        log.Record(order);
    }
}
```

The handler reads the headers with `TryGetValue`, because under `[FunctionTesting]` alone the
request has no headers.

In a local run, two events went to `orders` with the content type `application/json` and the
partition keys `A-1` and `A-2`. Their bodies were `{"id":"A-1","quantity":2}` and
`{"id":"A-2","quantity":1}`. The handler logged
`Order A-1 with partition key A-1 at sequence number 0` and
`Order A-2 with partition key A-2 at sequence number 1`.

Events with the same partition key go to one partition, in the order they were sent. An event with
no partition key goes to the partitions in turn.

## Failed events

An event fails when its handler throws or its body does not bind. The first failed event fails the
whole invocation with its exception. The events after it in the batch do not run. The request log
names the failed route, such as `STREAM /payments request failed`.

The function reports no single event back to the host. `ReportsItemFailures` is false and cannot be
turned on. The function uses the `Checkpoint` failure mode. [Triggers](/guide/triggers) covers
`BatchFailureMode`.

The module's `RetryCount` and `RetryDelay` settings give the stream functions a retry policy. The
Azure [Overview](/azure/) covers setting it. The retry policy decides what the host does with a
failed batch:

| Retry policy | What the host does with a failed batch |
|---|---|
| None | It moves the partition's checkpoint past the batch when the invocation ends. It does not deliver the batch again |
| `RetryCount` and `RetryDelay` set | It runs the whole batch again, up to `RetryCount` more times, `RetryDelay` apart. Then it moves the checkpoint past the batch |

Without a retry policy, the failed event and every event after it in the batch are never handled.
With a retry policy, the events that ran before the failure run again. Later events of the partition
wait until the retries end.

When an invocation does not complete, such as when its instance crashes, the checkpoint stays where
it was. The next instance reads the same events again. So an event can reach the handler more than
once.

An event stays in the hub for the hub's retention time. On the Standard tier the retention time is
1 hour by default and at most 7 days.

Microsoft's documentation covers
[the trigger](https://learn.microsoft.com/azure/azure-functions/functions-bindings-event-hubs-trigger),
[checkpoints](https://learn.microsoft.com/azure/azure-functions/functions-reliable-event-processing)
and [retry policies](https://learn.microsoft.com/azure/azure-functions/functions-bindings-error-pages).

## Module settings

The Event Hubs module has the settings in this table. None is required.

| Property | Default | What it sets |
|---|---|---|
| `Connection` | `AzureWebJobsEventHubs` | The app setting that holds the namespace's connection string |
| `ConsumerGroup` | `$Default` | The consumer group that every stream function reads through |
| `RetryCount`, `RetryDelay` | None | The retry policy under [Failed events](#failed-events). The Azure [Overview](/azure/) covers it |

To change a setting, put `[EventHubsModule(...)]` on the application class. The module is in
namespace `Hardened.Azure.Functions.EventHubs`. This application class names its own connection
setting and consumer group:

```csharp
using Hardened.Azure.Functions.EventHubs;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[EventHubsModule(Connection = "OrdersHub", ConsumerGroup = "analytics")]
public partial class Application;
```

The settings apply to every stream function of the application. Every hub that the application
reads needs the consumer group. When a hub lacks it, the listener of that hub's function does not
start. For a hub `audit` without the group, the host logs this message and goes on with the other
functions:

```text
The listener for function 'Functions.Stream_audit' was unable to start.
```

## Deploying

In an Event Hubs namespace, create each hub that a stream handler names. The app setting that
`Connection` names, `AzureWebJobsEventHubs` by default, holds the namespace's connection string.

In these commands, `orders` is the function app and its resource group, as the template's README
names them. `orders-ns` is the namespace. The last command creates the consumer group `analytics`,
which the example under [Module settings](#module-settings) reads through.

```bash
az eventhubs eventhub create --namespace-name orders-ns --resource-group orders --name orders

az functionapp config appsettings set --name orders --resource-group orders \
    --settings "AzureWebJobsEventHubs=$(az eventhubs namespace authorization-rule keys list \
        --namespace-name orders-ns --resource-group orders --name RootManageSharedAccessKey \
        --query primaryConnectionString --output tsv)"

az eventhubs eventhub consumer-group create --namespace-name orders-ns --resource-group orders \
    --eventhub-name orders --name analytics
```

Without the setting, the host does not start. It logs:

```text
EventHub account connection string with name 'AzureWebJobsEventHubs' does not exist in the settings. Make sure that it is a defined App Setting.
```

::: warning
The connection string has to be the namespace's. A connection string that names a hub, with
`EntityPath=` and the hub's name, makes `[Stream("orders")]` read that hub instead of `orders`. Its
events reach the `orders` handler under `STREAM /orders`. Every one that binds succeeds.
:::

The host keeps each partition's checkpoint in the storage account that `AzureWebJobsStorage` names,
in the blob container `azure-webjobs-eventhub`. With no checkpoint, the function starts at the
beginning of each partition. On its first start, and after a change of consumer group or storage
account, it reads every event the hub still holds.

A hub that another application also reads needs a consumer group for each application. A hub's
partition count is the most instances that can read it at once through one consumer group.

The host puts at most `extensions.eventHubs.maxEventBatchSize` events in a batch, 100 by default.
The template's `host.json` does not set it.

Locally, the template's `local.settings.json` points `AzureWebJobsEventHubs` at the Event Hubs
emulator, with `UseDevelopmentEmulator=true`. It points `AzureWebJobsStorage` at Azurite. The Azure
[Overview](/azure/) covers running the function app locally and deploying it. A function app runs
locally and deploys the same way for every trigger.

Microsoft's documentation covers
[consumer groups and partitions](https://learn.microsoft.com/azure/event-hubs/event-hubs-features)
and
[the trigger's `host.json` settings](https://learn.microsoft.com/azure/azure-functions/functions-bindings-event-hubs).

## Testing a stream handler

The template writes this test in `tests/Orders.Tests/OrderHandlerTests.cs`. The test sends an event
through the `Application.Streams` façade:

```csharp
using DependencyModules.xUnit.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderHandlerTests
{
    [ModuleTest]
    public async Task ARecordReachesTheHandler(Application.Streams streams, OrderLog log)
    {
        await streams.Orders(new Order { Id = "A-1", Quantity = 2 });

        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

The template's test project declares `[assembly: AzureFunctionsTesting]`. The attribute delivers
each call on the façade to the function as one batch of events, in order. Each event has the
message as its body and the content type `application/json`, and no application properties. The
Azure [Testing](/azure/testing) page covers the rest of what the attribute builds.

Under `[FunctionTesting]` alone the request has no headers. The handler that logs two headers passes
the template's test under both attributes. What a failed message does to the façade call depends on
the delivery. [Testing functions](/guide/testing-functions) covers it.

## Next

- [Triggers](/guide/triggers): the trigger attributes, payload binding and `BatchFailureMode`
- [Changes](/azure/change): the Cosmos DB change feed, the other source Azure delivers in order
- [Overview](/azure/): the packages, the entry point, running locally and the retry policy
- [Testing functions](/guide/testing-functions): testing a trigger handler
- [Testing](/azure/testing): testing on Azure

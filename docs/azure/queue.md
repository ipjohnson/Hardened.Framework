# Queues

`[Queue("orders")]` on a method makes it the handler for messages from the Azure Service Bus queue
named `orders`. The Functions host delivers the messages in batches, and the handler runs once for
each message, in the batch's order.

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Queue("orders")]
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

The project's reference to `Hardened.Azure.Functions.ServiceBus` makes `[Queue]` mean a Service Bus
queue. The build registers the package's `ServiceBusModule` on the application and writes an Azure
function for the handler. In the template, `Order` has a string `Id` and an int `Quantity`.
`OrderLog` is a `[SingletonService]` that keeps the orders it is given. [Triggers](/guide/triggers)
covers `[Queue]` and the other trigger attributes.

`func start` lists the app's function, then logs one invocation for a message sent to the queue
`orders`:

```text
Functions:

	Queue_orders: serviceBusTrigger

Executing 'Functions.Queue_orders' (Reason='(null)', Id=125ede44-9b38-43bd-aa69-ff8b6d50f9fe)
QUEUE /orders started
QUEUE /orders  finished status code '(null)'  duration 00:00:00.0213990
Executed 'Functions.Queue_orders' (Succeeded, Id=125ede44-9b38-43bd-aa69-ff8b6d50f9fe, Duration=144ms)
```

`Queue_orders` is the Azure function the build writes for the handler. `QUEUE /orders` is the route
the handler runs under. The host completes the message after the invocation succeeds. The queue is
then empty. The Azure [Overview](/azure/) covers running the app locally with `func start`.

## Packages

A queue function references `Hardened.Azure.Functions.ServiceBus` beside
`Hardened.Azure.Functions.Runtime` and `Microsoft.Azure.Functions.Worker.Sdk`. These are the two
Hardened references:

```xml
<PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Azure.Functions.ServiceBus" Version="0.0.0-HARDENED-VERSION" />
```

The same package serves `[Topic]` handlers, which read a topic through a subscription.
[Topics](/azure/topic) covers them. The other packages, `Program.cs`, `host.json` and the project
settings are the same for every Azure function app. The Azure [Overview](/azure/) covers them.

The `hardened-function` template writes this app and a test project:

```bash
dotnet new hardened-function -n Orders --host azure --trigger queue
```

## Functions and routes

The build writes one Azure function for each `[Queue]` handler. `[Queue("orders")]` gives the
function `Queue_orders`, bound to the queue `orders`. The function's `ServiceBusTrigger` carries
`IsBatched = true`, so the function receives messages in batches. The Azure [Overview](/azure/)
lists what each generated function binds, and gives the rule that names it.

The build writes the function's binding into `functions.metadata` beside the assembly. The host
reads the binding from that file. For the template's app,
`src/Orders/bin/Debug/net8.0/functions.metadata` holds this entry:

```json
[
  {
    "name": "Queue_orders",
    "scriptFile": "Orders.dll",
    "entryPoint": "Orders.Generated.ApplicationAzureFunctions.Queue_orders",
    "language": "dotnet-isolated",
    "properties": {
      "IsCodeless": false
    },
    "bindings": [
      {
        "name": "messages",
        "direction": "In",
        "type": "serviceBusTrigger",
        "queueName": "orders",
        "cardinality": "Many",
        "properties": {
          "supportsDeferredBinding": "True"
        }
      }
    ]
  }
]
```

`"cardinality": "Many"` is the batch. The binding names no `connection`, so the host reads the
connection from the app setting `AzureWebJobsServiceBus`. [Deploying](#deploying) shows how to set
it.

The host invokes `Queue_orders` only with messages from the queue `orders`. The handler runs under
the route `QUEUE /orders`, which comes from the function and not from the message. One function app
serves several queues, with a function for each handler.

A queue's name holds letters, digits, periods, hyphens, underscores and slashes, and starts and ends
with a letter or digit. Service Bus matches the name without regard to case
([Naming rules and restrictions for Azure resources](https://learn.microsoft.com/en-us/azure/azure-resource-manager/management/resource-name-rules)).
`[Queue("ORDERS")]` receives the messages of the queue `orders`, as the function `Queue_ORDERS`,
under the route `QUEUE /ORDERS`.

A period, a hyphen or a slash in a queue's name becomes `_` in the function's name.
`[Queue("orders.v2")]` gives `Queue_orders_v2`, and `[Queue("orders/eu")]` gives `Queue_orders_eu`.
The route keeps the name as written, such as `QUEUE /orders.v2`.

Two queue names that give one function name fail the build with `HRDAZ002`, which the Azure
[Overview](/azure/) covers. Names that differ only in case are among them. `[Queue("Orders")]`
beside `[Queue("orders")]` fails with `HRDAZ002` for `Queue_orders`.

The queue has to exist. Until it does, the host logs that the messaging entity could not be found,
about twice a second, and the handler does not run.

The messages of a session-enabled queue never reach the handler. The function does not turn
sessions on. The host logs this message about twice a second:

```text
It is not possible for an entity that requires sessions to create a non-sessionful message receiver.
```

## Body and headers

The message's body is the request body. The handler's parameter binds it as JSON whatever the
message's content type says. [Triggers](/guide/triggers) covers how the body binds to the handler's
parameter.

A message with an empty body fails a handler that binds a payload, with this `JsonException`:

```text
The input does not contain any JSON tokens. Expected the input to start with a valid JSON token, when isFinalBlock is true.
```

The Service Bus message is then a failed message. [Failed messages](#failed-messages) covers what
happens to it.

Each application property of the message becomes a header named for the property. The message id
and the delivery count become `x-azure-servicebus-message-id` and
`x-azure-servicebus-delivery-count`. The message's content type becomes `Content-Type` when the
message has one.

| Header | Carries |
|---|---|
| `x-azure-servicebus-message-id` | The message id, the same on every delivery |
| `x-azure-servicebus-delivery-count` | How many times Service Bus has delivered the message, starting at `1` |
| `Content-Type` | The message's content type, when it has one |
| The property's name | Each application property's value |

The message id, the delivery count and the content type are written after the properties. A
property named `x-azure-servicebus-message-id` or `x-azure-servicebus-delivery-count` is replaced by
the message's own value. A property named `Content-Type` arrives as it is when the message has no
content type, and is replaced by the content type when it has one. The request matches header names
without regard to case.

A property that is not a string arrives as text, formatted with the invariant culture:

| Property type | Sent | Arrives as |
|---|---|---|
| `string` | `acme` | `acme` |
| `int` | `5` | `5` |
| `long` | `9000000000` | `9000000000` |
| `double` | `1.5` | `1.5` |
| `bool` | `true` | `True` |
| `DateTimeOffset` | `2026-09-24T12:00:00+00:00` | `09/24/2026 12:00:00 +00:00` |
| `Guid` | `2f1c3e4a-1111-2222-3333-444455556666` | `2f1c3e4a-1111-2222-3333-444455556666` |
| `TimeSpan` | `00:01:30` | `00:01:30` |
| `Uri` | `https://example.com/a` | `https://example.com/a` |

The message's other system properties, such as `Subject`, `CorrelationId`, `SessionId`,
`SequenceNumber` and `EnqueuedTime`, do not reach the handler. A message delivered again keeps its
message id, and its delivery count is one higher on each delivery.

A handler reads the headers through an `IExecutionRequest` parameter, which is in namespace
`Hardened.Requests.Abstract.Execution`. This `src/Orders/OrderHandler.cs` reads two of them:

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
        request.Headers.TryGetValue("x-azure-servicebus-message-id", out var messageId);
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

This message, sent to the queue `orders`, reaches the handler:

| Message field | Value |
|---|---|
| Body | `{"id":"A-1","quantity":2}` |
| `ContentType` | `application/json` |
| `MessageId` | `order-A-1` |
| Application property `tenant` | `acme` |

The handler logs `Message order-A-1 for tenant acme`, and the host logs the invocation as
`Succeeded`. The example reads each header with `TryGetValue`, because a header can be missing.
Under `[FunctionTesting]` alone the request has none.

## Failed messages

A message fails when its handler throws or its body does not bind. The Functions host receives
messages in peek-lock mode. It completes them when the invocation succeeds and abandons them when it
fails
([Azure Service Bus trigger for Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-service-bus-trigger)).

By default, the first failed message fails the invocation with its exception, and the messages after
it in the batch do not run. The host then abandons every message of the batch, including the ones
the handler already handled. Service Bus delivers them again at once, each with its delivery count
one higher
([Message Transfers, Locks, and Settlement](https://learn.microsoft.com/en-us/azure/service-bus-messaging/message-transfers-locks-settlement)).
Service Bus can deliver a message more than once
([Azure Service Bus Queues, Topics, and Subscriptions](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-queues-topics-subscriptions)).
The message id identifies a repeat.

A message whose delivery count passes the queue's `MaxDeliveryCount`, 10 by default, moves to the
queue's dead-letter queue, with the dead-letter reason `MaxDeliveryCountExceeded`
([Service Bus Dead-Letter Queues](https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-dead-letter-queues)).
A message that keeps arriving in a batch with a failing message is dead-lettered with it, although
its handler succeeded.

With `ReportsItemFailures` on, a failed message does not fail the invocation. Every message of the
batch runs. The function completes each message whose handler succeeded and abandons each that
failed. The host logs the invocation as `Succeeded`. The handler's exception is still logged, under
`QUEUE /orders request failed`. A batch in which every message fails is still an invocation that
succeeds, and every message is abandoned. [Module settings](#module-settings) shows how to turn
`ReportsItemFailures` on.

| Case | `ReportsItemFailures` off | `ReportsItemFailures` on |
|---|---|---|
| Every handler returns | The invocation succeeds, and the host completes the batch | The invocation succeeds, and the function completes each message |
| One handler throws, or one body does not bind | The invocation fails, the later messages do not run, and the host abandons the whole batch | The invocation succeeds, every message runs, and only the failed message is abandoned |
| Every message fails | The invocation fails at the first message | The invocation succeeds, and every message is abandoned |
| The batch outlives the lock | The invocation succeeds, completing fails, and the batch comes back | The invocation fails when completing, and the batch comes back |

## Lock duration and batch size

::: warning
The host renews no lock for a batch, and every `[Queue]` function receives batches. A batch whose
handlers run longer than the queue's lock duration, 1 minute by default, is delivered again after
every handler succeeded. After `MaxDeliveryCount` deliveries, its messages go to the dead-letter
queue. Keep a batch within the lock. Raise the queue's lock duration, up to 5 minutes, or lower
`maxMessageBatchSize`.
:::

Each such invocation logs `Succeeded`, and the host then logs:

```text
The lock supplied is invalid. Either the lock expired, or the message has already been removed from the queue.
```

With `ReportsItemFailures` on, the same batch fails its invocation instead, because completing a
message whose lock expired throws.

`maxMessageBatchSize` under `extensions.serviceBus` in `host.json` limits how many messages one
batch holds, 1000 by default
([Azure Service Bus bindings for Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-service-bus)).
This `src/Orders/host.json` limits a batch to ten messages:

```json
{
  "version": "2.0",
  "extensions": {
    "serviceBus": {
      "maxMessageBatchSize": 10
    }
  }
}
```

## The dead-letter queue

Service Bus keeps a dead-lettered message until something receives it from the dead-letter queue,
`orders/$deadletterqueue`. A handler cannot read a dead-letter queue.
`[Queue("orders/$deadletterqueue")]` fails the build with `HardenedException`, because the `$` in
the name stops the function generator:

```text
CSC : error HardenedException: The generator threw and produced no source: ArgumentException: The hintName 'QUEUE.orders/$deadletterqueue.FunctionHandler.cs' contains an invalid character '$' at position 13. (Parameter 'hintName') at Microsoft.CodeAnalysis.AdditionalSourcesCollection.Void Add(System.String, Microsoft.CodeAnalysis.Text.SourceText)
```

## Module settings

`ServiceBusModule` has three settings. A queue reads `Connection` and `ReportsItemFailures`, and a
topic also reads `Subscription`. [Topics](/azure/topic) covers `Subscription`.

| Property | Default | Effect |
|---|---|---|
| `Connection` | Unset: the app setting `AzureWebJobsServiceBus` | The name of the app setting that holds the connection to the namespace |
| `ReportsItemFailures` | `false` | A failed message does not fail the invocation. Each message is completed or abandoned on its own |
| `Subscription` | Unset | The subscription every `[Topic]` handler reads through. A `[Topic]` handler needs it |

To change a setting, put `[ServiceBusModule(...)]` on the application class. The module is in
namespace `Hardened.Azure.Functions.ServiceBus`. The build then registers only the module the
application declares. This application settles failed messages one by one:

```csharp
using Hardened.Azure.Functions.ServiceBus;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[ServiceBusModule(ReportsItemFailures = true)]
public partial class Application;
```

The build also writes the settings into the function's binding. `ReportsItemFailures = true` adds
`"autoCompleteMessages": false`, which stops the host completing messages itself. The settings apply
to every Service Bus function of the application, queues and topics alike.

This application reads its connection from the app setting `OrdersServiceBus`:

```csharp
using Hardened.Azure.Functions.ServiceBus;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[ServiceBusModule(Connection = "OrdersServiceBus")]
public partial class Application;
```

`Connection = "OrdersServiceBus"` adds `"connection": "OrdersServiceBus"` to the binding.

Each setting has to be a literal. A constant, or any other expression, fails the build with
`HRDAZ004`. This error is for `[ServiceBusModule(Connection = Settings.ServiceBus)]`, where
`Settings.ServiceBus` is a `const string` in namespace `Orders`:

```text
error HRDAZ004: Connection on [ServiceBusModule] is written as global::Orders.Settings.ServiceBus, and the function metadata the host indexes needs its value at build. Write it as a string literal, an integer literal, or true or false.
```

## Deploying

A queue function needs the queue, and an app setting that connects the function app to the queue's
namespace. The queue's name is the name in `[Queue]`. In these commands, the resource group is
`orders`, the namespace `orders-ns` and the function app `orders-functions`:

```bash
az servicebus queue create \
    --resource-group orders \
    --namespace-name orders-ns \
    --name orders \
    --lock-duration PT1M \
    --max-delivery-count 10

connection=$(az servicebus namespace authorization-rule keys list \
    --resource-group orders \
    --namespace-name orders-ns \
    --name RootManageSharedAccessKey \
    --query primaryConnectionString \
    --output tsv)

az functionapp config appsettings set \
    --resource-group orders \
    --name orders-functions \
    --settings "AzureWebJobsServiceBus=$connection"
```

`--lock-duration` and `--max-delivery-count` set the queue's lock duration and `MaxDeliveryCount`.
The first command writes their defaults, `PT1M` and `10`
([az servicebus queue](https://learn.microsoft.com/en-us/cli/azure/servicebus/queue)).

`az servicebus namespace authorization-rule keys list` reads the namespace's connection string.
`az functionapp config appsettings set` writes it to the app setting `AzureWebJobsServiceBus`. The
connection string is the namespace's, not one limited to the queue. With
`[ServiceBusModule(Connection = "OrdersServiceBus")]`, the setting's name is `OrdersServiceBus`.

An identity-based connection sets `AzureWebJobsServiceBus__fullyQualifiedNamespace` to the
namespace's host name, such as `orders-ns.servicebus.windows.net`, in place of the connection
string:

```bash
az functionapp config appsettings set \
    --resource-group orders \
    --name orders-functions \
    --settings "AzureWebJobsServiceBus__fullyQualifiedNamespace=orders-ns.servicebus.windows.net"
```

The function app's identity then needs the Azure Service Bus Data Receiver role
([Configure connections to remote services in Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/manage-connections)).
With the Azure Service Bus Data Owner role, or a connection string with the Manage right, the host
also reads the queue's message count for scaling.

Without the app setting, the function app starts and no message reaches the handler. For the
application with `Connection = "OrdersServiceBus"` and no such setting, the host logs this:

```text
The listener for function 'Functions.Queue_orders' was unable to start. Microsoft.Azure.WebJobs.Extensions.ServiceBus: Service Bus account connection string with name 'OrdersServiceBus' does not exist in the settings. Make sure that it is a defined App Setting.
```

Locally, `local.settings.json` holds the same setting. The template points `AzureWebJobsServiceBus`
at the Service Bus emulator. The emulator's queues are declared in its configuration file or created
with the Service Bus administration client
([Azure Service Bus Emulator Overview and Key Features](https://learn.microsoft.com/en-us/azure/service-bus-messaging/overview-emulator)).
The Azure [Overview](/azure/) covers running locally. It also covers creating and deploying the
function app, which is the same for every trigger.

## Testing

`Application.Queues` is the façade a test calls. [Testing functions](/guide/testing-functions)
covers it. The template's test project declares `[assembly: AzureFunctionsTesting]` beside
`[assembly: FunctionTesting]`. The template writes this `tests/Orders.Tests/OrderHandlerTests.cs`
for `--trigger queue`:

```csharp
using Hardened.Shared.Testing.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderHandlerTests
{
    [HardenedTest]
    public async Task AMessageReachesTheHandler(Application.Queues queues, OrderLog log)
    {
        await queues.Orders(new Order { Id = "A-1", Quantity = 2 });

        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }

    [HardenedTest]
    public async Task EveryMessageInABatchIsHandled(Application.Queues queues, OrderLog log)
    {
        await queues.Orders(
            new Order { Id = "A-1" },
            new Order { Id = "A-2" },
            new Order { Id = "A-3" }
        );

        Assert.Equal(3, log.Orders.Count);
    }
}
```

Under `[AzureFunctionsTesting]`, each façade call is one invocation of the queue's function, with one
`ServiceBusReceivedMessage` for each message of the call. Each message's id is the queue's name and
the message's position, such as `orders-0` and `orders-1`. Its delivery count is 1, its content type
is `application/json`, and it has no application properties. Under `[FunctionTesting]` alone the
request has no headers. The handler in [Body and headers](#body-and-headers) passes the template's
two tests under both.

A failed message fails the façade call under both, and the messages after it do not run.
`[ServiceBusModule(ReportsItemFailures = true)]` does not change this under
`[AzureFunctionsTesting]`, unless the test's container holds a `ServiceBusMessageActions` for the
delivery to settle through. [Testing](/azure/testing) covers `[AzureFunctionsTesting]` and
`RecordingMessageActions`, which records the messages a test's invocation completed and abandoned.

## Next

- [Topics](/azure/topic): Service Bus topics, read through a subscription by the same package
- [Triggers](/guide/triggers): the trigger attributes, payload binding and `BatchFailureMode`
- [Overview](/azure/): the packages, the entry point, running locally and deploying the function app
- [Testing](/azure/testing): what `[AzureFunctionsTesting]` builds, and settling messages in a test
- [Testing functions](/guide/testing-functions): testing a trigger handler

# Queues

A queue handler receives one message at a time. Service Bus delivers a batch to the function, and
the batch filter runs the pipeline once per message, so the request a handler sees is the message.

```csharp
using Hardened.Functions.Runtime.Attributes;

public class OrderHandlers {

    [Queue("orders")]
    public void OnOrder(Order order, IOrderStore store) => store.Place(order);
}
```

Nothing above names Service Bus, and the application names nothing at all:

```csharp
[HardenedModule]
public partial class Application;
```

Referencing `Hardened.Azure.Functions.ServiceBus` is what decides that `[Queue]` means a Service
Bus queue. See [Triggers](/guide/triggers) for the vocabulary.

## Packages

```xml
<PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="2.1.0" />
<PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.34.0-rc1000" />
<PackageReference Include="Hardened.Azure.Functions.ServiceBus" Version="0.34.0-rc1000" />
<PackageReference Include="Hardened.Azure.Functions.SourceGenerator" Version="0.34.0-rc1000" PrivateAssets="all" />
```

The same package serves `[Topic]`, from a subscription; see [Topics](/azure/topic).

`dotnet new hardened-function --host azure --trigger queue` writes this shape with tests.

## The function

`[Queue("orders")]` becomes a function named `Queue_orders`, bound to the queue by the Service Bus
extension's own trigger attribute:

```csharp
[Function("Queue_orders")]
public static Task Queue_orders(
    [ServiceBusTrigger("orders", IsBatched = true)] ServiceBusReceivedMessage[] messages,
    ServiceBusMessageActions messageActions,
    FunctionContext context) => ...
```

The function is what the host indexes and what `func start` lists. Its route is `QUEUE /orders`,
from the function rather than from the message: the host binds one function to one queue, so the
function's identity says which queue delivered.

The connection is the extension's default, the app setting `AzureWebJobsServiceBus`. A function
app reading from another namespace names the setting on the module:

```csharp
[HardenedModule]
[ServiceBusModule(Connection = "OrdersServiceBus")]
public partial class Application;
```

That is the one reason to write `[ServiceBusModule]` out for a queue; without it the generator
registers the module itself.

## What the handler sees

The message body is the request body, so a JSON message binds the way a JSON web request does,
and `Content-Type` is the message's content type when the publisher set one. The message's
application properties become request headers under their own names. Beside them:

| Header | Carries |
|---|---|
| `x-azure-servicebus-message-id` | The message id, the same on every redelivery |
| `x-azure-servicebus-delivery-count` | How many times the queue has delivered the message |

The two are written after the properties, so a property named like one of them does not replace
it. A binary property is not a header and is left out.

## What a failure means

With the default settings, the host settles the batch on the invocation's outcome: a handler that
returns normally has its message completed, and a thrown exception fails the invocation, which
abandons every message in the batch. The queue delivers them again, up to its
`MaxDeliveryCount`, and then dead-letters them.

That is `PerItem` in the framework's [vocabulary](/guide/triggers#batches-and-what-a-failure-means),
and the whole batch is what a failure costs, as it is on SQS without item reporting. To settle
message by message, turn the report on:

```csharp
[HardenedModule]
[ServiceBusModule(ReportsItemFailures = true)]
public partial class Application;
```

The generated binding then says `autoCompleteMessages: false`, the batch filter attempts every
message, and the adapter completes each that succeeded and abandons each that failed, so the queue
delivers the refused messages alone and the invocation succeeds. A batch whose pipeline never
finished is settled by nobody; its locks expire and the messages come back together, which is
what the host would have done.

A handler that outlives the lock sees the same message arrive again on another instance while it
is still running, so keep queue handlers shorter than the queue's lock duration, or make them
idempotent, or both.

## Deploying

The queue and the setting that reaches it:

```bash
az servicebus queue create --namespace-name orders-ns --resource-group orders --name orders
az functionapp config appsettings set --name orders --resource-group orders \
    --settings "AzureWebJobsServiceBus=$(az servicebus namespace authorization-rule keys list \
        --namespace-name orders-ns --resource-group orders --name RootManageSharedAccessKey \
        --query primaryConnectionString --output tsv)"
```

Locally, `local.settings.json` carries the same setting, pointed at the Service Bus emulator:

```json
"AzureWebJobsServiceBus": "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
```

## Testing

The generated façade sends through the real pipeline. Passing several payloads sends one batch:

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

Adding `[assembly: AzureFunctionsTesting]` beside `[assembly: FunctionTesting]` builds the
`ServiceBusReceivedMessage` batch the worker would bind and hands it to the real invocation
handler, so the message headers and the settlement are exercised too. A settlement test hands in
a `RecordingMessageActions` and asserts what was completed and what was abandoned. No test method
changes. See [Testing Azure handlers](/azure/testing).

## Next

- [Topics](/azure/topic): the same package, read through a subscription
- [Triggers](/guide/triggers): the failure semantics in full
- [Testing Azure handlers](/azure/testing): the three rungs

# Topics

A topic handler receives one message at a time, from a subscription of the topic:

```csharp
using Hardened.Functions.Runtime.Attributes;

public class OrderEventHandlers {

    [Topic("order-events")]
    public void OnOrderEvent(OrderEvent orderEvent, IProjection projection) => projection.Apply(orderEvent);
}
```

## Packages

```xml
<PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="2.1.0" />
<PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.32.0-rc1000" />
<PackageReference Include="Hardened.Azure.Functions.ServiceBus" Version="0.32.0-rc1000" />
<PackageReference Include="Hardened.Azure.Functions.SourceGenerator" Version="0.32.0-rc1000" PrivateAssets="all" />
```

The same package serves `[Queue]`; the delivery, the headers and the settlement are the ones
[Queues](/azure/queue) describes.

`dotnet new hardened-function --host azure --trigger topic` writes this shape with tests.

## The subscription is the module's

Service Bus reads a topic through a subscription, and a subscription is named for the consumer,
so it is not something a handler that names the topic can know. It is written once on the
application:

```csharp
[HardenedModule]
[ServiceBusModule(Subscription = "projection")]
public partial class Application;
```

Every `[Topic]` handler in the application reads through that subscription. Leaving it out is
`HRDAZ003` at build, naming the property and where to write it, because the binding cannot be
generated without it. The value has to be a literal: the host reads it from metadata the build
writes, not from the compiled attribute, and a constant from another class is `HRDAZ004`.

`[Topic("order-events")]` becomes `Topic_order_events`, bound by
`[ServiceBusTrigger("order-events", "projection", IsBatched = true)]` and routed as
`TOPIC /order-events`. Two applications reading the same topic are two subscriptions, one each.

## Deploying

```bash
az servicebus topic create --namespace-name orders-ns --resource-group orders --name order-events
az servicebus topic subscription create --namespace-name orders-ns --resource-group orders \
    --topic-name order-events --name projection
```

The connection is `AzureWebJobsServiceBus`, set as [Queues](/azure/queue#deploying) shows, or the
setting `[ServiceBusModule(Connection = "...")]` names.

## What a failure means

The same as a queue's: the batch is abandoned and delivered again unless `ReportsItemFailures`
is on, and then only the refused messages come back. The subscription's `MaxDeliveryCount` is
where they stop.

## Testing

```csharp
[HardenedTest]
public async Task AnEventIsProjected(Application.Topics topics, IProjection projection) {
    await topics.OrderEvents(new OrderEvent { Id = "A-1" });

    Assert.Contains("A-1", projection.Applied);
}
```

The façade is named for the topic. Under `[assembly: AzureFunctionsTesting]` the message is the
`ServiceBusReceivedMessage` the worker would bind, and `MetadataAgreement` checks that the
subscription the build wrote into `functions.metadata` is the one the generated provider tells the
host. See [Testing Azure handlers](/azure/testing).

## Next

- [Queues](/azure/queue): the delivery, the headers and the settlement in full
- [Events](/azure/event): Event Grid, for events that are not Service Bus messages

# Events

`[Event]` serves any CloudEvent Event Grid delivers, addressed by its source and its type:

```csharp
using Hardened.Functions.Runtime.Attributes;

public class OrderEventHandlers {

    [Event("com.acme.orders", "OrderPlaced")]
    public void OnOrderPlaced(Order order, IFulfilment fulfilment) => fulfilment.Start(order);
}
```

The event's data is the request body, so a JSON event binds its data the way a web request binds
its body.

## Packages

```xml
<PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="2.1.0" />
<PackageReference Include="Hardened.Azure.Functions.Runtime" Version="0.33.0-rc1000" />
<PackageReference Include="Hardened.Azure.Functions.EventGrid" Version="0.33.0-rc1000" />
<PackageReference Include="Hardened.Azure.Functions.SourceGenerator" Version="0.33.0-rc1000" PrivateAssets="all" />
```

The adapter reads the event through `Hardened.CloudEvents`, which names no cloud and is the same
reader Eventarc's events go through on Cloud Run.

## One function for every handler

Unlike the entity-bound families, `[Event]` gets one function, named `Event`, bound by
`[EventGridTrigger]`: an Event Grid subscription delivers everything that matches it to one
function, and which handler runs is a question about the event. The adapter routes each event as
`EVENT /{source}/{type}` from its `source` and `type` attributes, so
`[Event("com.acme.orders", "OrderPlaced")]` is `EVENT /com.acme.orders/OrderPlaced`.

The subscription has to deliver in the CloudEvents schema, which is where the source and type
are:

```bash
az eventgrid event-subscription create --name orders-to-fulfilment \
    --source-resource-id "$(az eventgrid topic show --name orders --resource-group orders --query id --output tsv)" \
    --endpoint "$(az functionapp show --name fulfilment --resource-group orders --query id --output tsv)/functions/Event" \
    --endpoint-type azurefunction \
    --event-delivery-schema cloudeventschemav1_0 \
    --included-event-types OrderPlaced
```

An event that arrives with a source and type no handler declared fails the invocation, so Event
Grid retries it and, in the end, dead-letters it, rather than the function acknowledging
something it did nothing with. The subscription's filters are the place to narrow what arrives.

## What the handler sees

The data as the body, with `Content-Type` set to the event's `datacontenttype`, or
`application/json` when the producer declared none. The event's attributes as headers:
`ce-specversion`, `ce-id`, `ce-source`, `ce-type`, `ce-subject`, `ce-time`, `ce-dataschema`,
the same names a handler reads on Cloud Run. A structured event whose data was base64 is decoded.

## What a failure means

A thrown exception fails the invocation, and Event Grid retries the delivery on its schedule for
up to 24 hours and then dead-letters it, when the subscription has a dead-letter destination. One
event per invocation, no batch.

## Testing

`[Event]` has no generated façade, because it is addressed by two segments where every other
trigger has one. A test sends through the delivery itself, which is what a façade would have
called:

```csharp
[HardenedTest]
public async Task AnEventReachesItsHandler(ITriggerDelivery delivery, [Mock] IFulfilment fulfilment) {
    await delivery.Deliver([new Order { Id = "A-1" }], "EVENT", "/com.acme.orders/OrderPlaced");

    fulfilment.Received().Start(Arg.Is<Order>(order => order.Id == "A-1"));
}
```

Under `[assembly: AzureFunctionsTesting]` that is a structured CloudEvent, as JSON, handed to the
real invocation handler and parsed by the adapter. See [Testing Azure handlers](/azure/testing).

## Next

- [Blobs](/azure/blob): the Storage event, with its own adapter
- [Topics](/azure/topic): Service Bus, for messages rather than events
- [Triggers](/guide/triggers): the vocabulary

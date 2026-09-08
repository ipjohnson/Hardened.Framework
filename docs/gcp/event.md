# Events

`[Event]` serves any CloudEvent Eventarc delivers, addressed by its source and its type:

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
<PackageReference Include="Hardened.Gcp.CloudRun.Runtime" Version="0.30.0-rc1000" />
<PackageReference Include="Hardened.Gcp.CloudRun.Eventarc" Version="0.30.0-rc1000" />
```

The adapter reads the event through `Hardened.CloudEvents`, which names no cloud. Binary mode,
where the attributes are `ce-*` headers and the data is the body, and structured mode, where the
whole event is one `application/cloudevents+json` document, are both read.

## The route is the source and the type

`[Event("com.acme.orders", "OrderPlaced")]` routes as `EVENT /com.acme.orders/OrderPlaced`, from
`ce-source` and `ce-type`. An Eventarc trigger delivers whatever its filters match, and the
filters are where the deployment decides which events reach the service:

```bash
gcloud eventarc triggers create order-placed \
    --location=us-central1 \
    --destination-run-service=fulfilment \
    --event-filters="type=OrderPlaced" \
    --event-filters="source=com.acme.orders" \
    --channel=projects/my-project/locations/us-central1/channels/acme
```

An event that arrives with a source and type no handler declared is answered 500, so Eventarc
retries it and reports it, rather than the service acknowledging something it did nothing with. A
trigger is the place to narrow what arrives.

The other adapters recognise their own events first. A `google.cloud.storage.object.v1.finalized`
event reaches `[Blob]` when the Storage package is referenced, and reaches `[Event]` when it is
not; the same holds for Pub/Sub and Firestore. `[Event]` is the fallback for what nothing more
specific claims.

## What the handler sees

The data as the body, with `Content-Type` set to the event's `datacontenttype`. The event's
attributes as headers: `ce-specversion`, `ce-id`, `ce-source`, `ce-type`, `ce-subject`, `ce-time`,
`ce-dataschema` and any extension attribute, whichever mode the event arrived in. A structured
event whose data was base64 is decoded.

## What a failure means

A success answer acknowledges the event. A thrown exception answers 500 and Eventarc redelivers
with the retry policy of its transport. One event per request, no batch.

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

Under `[assembly: CloudRunTesting]` that is a binary-mode CloudEvent posted to the test's host. The
wire shape can also be posted by hand through `ITestWebApp`, with the `ce-*` headers on the request
and the data as its body. See [Testing Cloud Run handlers](/gcp/testing).

## Next

- [Topics](/gcp/topic): the Pub/Sub event, with its own adapter
- [Blobs](/gcp/blob) and [Changes](/gcp/change): the Storage and Firestore events, likewise
- [Triggers](/guide/triggers): the vocabulary

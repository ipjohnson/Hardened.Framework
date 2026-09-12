# Changes

`[Change]` carries a document after an edit, and on request the document before it:

```csharp
using Hardened.Functions.Runtime.Attributes;

public class OrderChangeHandlers {

    [Change("orders")]
    public void OnOrderChanged(Order order, IOrderProjection projection) =>
        projection.Apply(order);
}
```

Firestore delivered a protobuf `DocumentEventData` with typed values, and the handler declares
`Order`. The adapter turns the document's fields into plain JSON before they reach the handler, the
way the DynamoDB adapter unwraps a type-tagged item, so nothing in the handler knows the wire form.

## Packages

```xml
<PackageReference Include="Hardened.Gcp.CloudRun.Runtime" Version="0.34.0-rc1000" />
<PackageReference Include="Hardened.Gcp.CloudRun.Firestore" Version="0.34.0-rc1000" />
```

This is the one adapter in the line with a Google dependency. Firestore events are
`application/protobuf`, decoded with `Google.Events.Protobuf`, and that dependency is why the
package is its own rather than part of the Eventarc one: a service that handles no documents
carries no protobuf.

`dotnet new hardened-function --host gcp --trigger change` writes this shape with tests.

## The collection is the route

Firestore events arrive through Eventarc with the document's path in `ce-subject`,
`documents/orders/o-1`. `[Change("orders")]` routes on the collection the document is in, so one
handler serves every document of the collection and the trigger's path pattern decides which
documents fire:

```bash
gcloud eventarc triggers create orders-changed \
    --location=nam5 \
    --destination-run-service=projections \
    --destination-run-region=us-central1 \
    --event-filters="type=google.cloud.firestore.document.v1.written" \
    --event-filters="database=(default)" \
    --event-filters-path-pattern="document=orders/{id}" \
    --event-data-content-type="application/protobuf" \
    --service-account=eventarc-invoker@my-project.iam.gserviceaccount.com
```

The four event types, `created`, `updated`, `deleted` and `written`, all reach the same handler. A
trigger on `written` sees all three changes; a trigger on `deleted` sees only deletes.

## What the handler sees

The document after the change, as JSON, bound to the handler's parameter. On a delete there is no
document after the change, so the body is the document as it was, and the event's type says so.
Two headers carry the document's identity:

| Header | Carries |
|---|---|
| `x-goog-firestore-document` | The document's path, `orders/o-1` |
| `x-goog-firestore-document-name` | The document's full resource name |

The CloudEvent's own attributes are on the request as `ce-*` headers, so `ce-type` says which of
the four changes this was.

## The document before the change

A handler that has to know what changed binds the previous document beside the current one:

```csharp
using Hardened.Gcp.CloudRun.Firestore;

[Change("audit")]
public void OnAuditChanged(
    Order order,
    [OldValue] Order? previous,
    IOrderProjection projection) => projection.Apply(order, previous);
```

`[OldValue]` binds the previous document as the handler's own type, through the same deserializer
the body goes through. `[OldValue] Document? previous` binds it in Firestore's own form, with the
typed values still on.

It is null on a create, where there was no previous document, and null on an event whose trigger
was not configured to carry one. That second case is a deployment decision the handler cannot
see, so treat null as "not carried" rather than "no previous document". A parameter that cannot
hold null asks for something the event may not have, and is refused rather than bound to a
default.

Bind the document as well as the old value, not the old value alone. The document is what the
handler is about, and it is what the test façade sends.

## What a failure means

A success answer acknowledges the event. A thrown exception answers 500 and Eventarc redelivers
it. One document per event, so there is no batch and no report. Eventarc does not promise order
across documents, and a redelivered event arrives after later ones may have been handled, so write
these handlers to be idempotent.

Cosmos DB on Azure carries no previous document at all, and DynamoDB carries both images always.
`[OldValue]` is the one place the three change feeds diverge, and the divergence is written here
rather than hidden.

## Testing

```csharp
[HardenedTest]
public async Task AChangedDocumentReachesTheProjection(Application.Changes changes, IOrderProjection p) {
    await changes.Orders(new Order { Id = "o-1", Total = 42.5m });

    // ...
}
```

`Changes` for `[Change]`, with a method per collection. Under `[assembly: CloudRunTesting]` the
payload is written as a Firestore document, both as the value and as the old value, inside the
protobuf event Eventarc actually sends, so the decoder and the JSON projection are exercised too.
See [Testing Cloud Run handlers](/gcp/testing).

## Next

- [Events](/gcp/event): the same events without the adapter
- [Blobs](/gcp/blob): the other Eventarc source with an adapter of its own
- [Triggers](/guide/triggers): the vocabulary

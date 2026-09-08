# Streams and change feeds

Two ordered sources, two triggers. `[Change]` carries a row before and after an edit; `[Stream]`
carries records a publisher wrote. Both deliver a batch, and both rewind when an item fails.

```csharp
using Hardened.Functions.Runtime.Attributes;

public class OrderChangeHandlers {

    [Change("orders")]
    public void OnOrderChanged(Order order, IOrderProjection projection) =>
        projection.Apply(order);
}
```

DynamoDB delivered `{"id":{"S":"a-1"},"total":{"N":"42.5"}}` and the handler declares `Order`.
Nothing in it knows the item arrived type-tagged — the adapter unwraps the store's wire form before
it reaches the handler.

## Packages

| Trigger | Package | AWS source |
|---|---|---|
| `[Change("orders")]` | `Hardened.Aws.Lambda.DynamoDb` | DynamoDB Streams |
| `[Stream("clickstream")]` | `Hardened.Aws.Lambda.Kinesis` | Kinesis Data Streams |

Two packages rather than one, because two packages binding a single build property would resolve
first-import-wins on an import order nobody controls. `dotnet new hardened-function --trigger
change` and `--trigger stream` write each shape with tests.

`[Change]` names the table, not the stream. A stream's ARN contains a timestamp, so it changes when
a table's stream is disabled and re-enabled, and a handler's route would become a deployment detail.

## A stream binds the publisher's bytes

```csharp
[Stream("clickstream")]
public void OnClick(Click click, IClickSink sink) => sink.Record(click);
```

Kinesis carried base64 of this object and said nothing about what is in it, so the parameter type is
the contract between you and whoever writes. The transport adds no envelope the handler has to know
about — the same arrangement a direct invocation has.

That is the whole difference from `[Change]`. Same delivery shape, different vocabularies, which is
why the same handler cannot be moved between the two by changing the attribute alone.

## Reaching the raw image

A bound parameter cannot say everything DynamoDB stored: an absent attribute against a null one, a
set against a list, a number stored more precisely than the handler's type can hold. `[NewImage]`
and `[OldImage]` reach the record as it arrived.

```csharp
using Amazon.Lambda.DynamoDBEvents;
using Hardened.Aws.Lambda.DynamoDb;

[Change("audit")]
public void OnAuditChanged(
    Order order,
    [NewImage] IDictionary<string, DynamoDBEvent.AttributeValue> image,
    IOrderProjection projection) => projection.Raw(image);
```

Bind the row as well as the image, not the image alone. The row is what the handler is about and the
image is the detail it occasionally needs. It is also what the test façade requires: the generated
method takes the payload type the handler binds, so a handler binding only an image is sent nothing
and cannot be given one.

The image comes off the request rather than an injected singleton, so two records in flight cannot
see each other's.

## What a failure means

An ordered source fails differently from a queue. Naming an item tells the transport to redeliver
the batch from that item onward, so everything after a failure is coming back regardless. The
runtime stops at the first failure rather than attempting the rest — running them anyway would run
them twice, which a handler that is not idempotent experiences as a duplicate write.

Stopping is also what preserves the ordering the shard exists to give. A stream promises items in
order per partition key, and handling item five after item three failed breaks that promise on the
retry, where three is replayed after five has already been applied.

That is `BatchFailureMode.Checkpoint`, and it is the mode both these adapters use.

::: warning Make these handlers idempotent
An item that succeeded before a later one failed is delivered again on the retry. This is the
transport's design, not the framework's, and no setting turns it off.
:::

Reporting has to match the deployment here too:

```csharp
[HardenedModule]
[DynamoDbStreamsModule(ReportBatchItemFailures = true)]
public partial class Application;
```

With it off, the first failure fails the invocation and the whole batch is redelivered. Note that
SQS and Kinesis are answered with the identical `batchItemFailures` array and mean different things
by it — SQS deletes everything not named, Kinesis rewinds to the earliest name — which is why the
mode is a property of the adapter rather than a setting.

## Testing

```csharp
[HardenedTest]
public async Task AChangedRowReachesTheProjection(Application.Changes changes, IOrderProjection p) {
    await changes.Orders(new Order { Id = "a-1", Total = 42.5m });

    // ...
}
```

`Changes` for `[Change]`, `Streams` for `[Stream]`. Passing several payloads sends a batch. Adding
`[assembly: LambdaTesting]` packs each into the envelope DynamoDB or Kinesis actually sends, so the
type-tagged wire form and the failure report are exercised too.

## Next

- [Triggers](/guide/triggers): all seven sources, and the failure modes in full
- [Queues and topics](/aws/sqs): the unordered sources
- [Testing AWS handlers](/aws/testing): the two fidelity levels

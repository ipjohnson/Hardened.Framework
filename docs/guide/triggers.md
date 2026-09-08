# Triggers

A handler names the source it serves and nothing else. Which adapter delivers to it is decided by
the runtime package the project references, so moving an application to another cloud is a package
reference rather than a rewrite.

```csharp
using Hardened.Functions.Runtime.Attributes;

public class OrderHandler(OrderLog log) {

    [Queue("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

Nothing in that file names SQS, and nothing in the application module does either. The build reads
the `HardenedQueueModule` property that `Hardened.Aws.Lambda.Sqs` declares and registers the SQS
adapter for you. Reference a different provider's queue package and the same handler serves that
queue instead.

The attributes live in `Hardened.Functions.Runtime`, which names no cloud. See
[AWS](/aws/) for the adapter packages that serve them on Lambda.

## The seven sources

| Attribute | Delivers | AWS adapter | Build property |
|---|---|---|---|
| `[Queue(name)]` | Messages from a queue, one at a time | `Hardened.Aws.Lambda.Sqs` | `HardenedQueueModule` |
| `[Topic(name)]` | Messages published to a topic | `Hardened.Aws.Lambda.Sns` | `HardenedTopicModule` |
| `[Timer(name)]` | A schedule firing | `Hardened.Aws.Lambda.EventBridge` | `HardenedTimerModule` |
| `[Event(source, detailType)]` | An event from a message bus | `Hardened.Aws.Lambda.EventBridge` | `HardenedEventModule` |
| `[Change(table)]` | A row before and after an edit | `Hardened.Aws.Lambda.DynamoDb` | `HardenedChangeModule` |
| `[Stream(name)]` | Records from a sharded stream | `Hardened.Aws.Lambda.Kinesis` | `HardenedStreamModule` |
| `[Blob(bucket)]` | An object in a store changing | `Hardened.Aws.Lambda.S3` | `HardenedBlobModule` |

`[HardenedFunction]` is the eighth and the odd one: nothing delivers to it, a caller invokes it. It
binds `HardenedInvokeModule` the same way, and for the same reason — an application that had to
write `[InvokeModule]` itself would name a cloud in the one file that must not.

The web verbs are on the same mechanism. `[Get]`, `[Post]`, `[Put]`, `[Patch]` and `[Delete]` all
bind `HardenedHttpModule`, so a controller with a GET and a POST registers one adapter. That is why
a Lambda web host names `[ApiGatewayModule]` and a function host names nothing: the handlers are in
the host's own compilation in one case and in a referenced library in the other, and a generator
only sees the compilation it runs in.

## Naming the source

Every trigger takes the source's own name — the queue name, the bucket name, the table name — not
an ARN, a URL or a connection string. A name is the only part of a source's identity that survives
an account, a region and a provider, and it is what the adapter reads back off the delivered event
to route on. The full address is the deployment's business.

`[Event]` takes two, because a bus carries events from many publishers: `Source` says who published
it and `DetailType` says what happened.

```csharp
[Event("com.acme.orders", "OrderPlaced")]
public void OnOrderPlaced(OrderPlaced placed) => _projections.Apply(placed);
```

`[Change]` names the table rather than the stream. A stream's address contains a timestamp, so it
changes when a table's stream is disabled and re-enabled, and a handler's route would become a
deployment detail.

## Routing

A trigger routes through the same table as an HTTP route, because the method slot on a request was
always a string. `[Queue("orders")]` is `QUEUE /orders`; `[Get("/orders/{id}")]` is
`GET /orders/{id}`. One table, one lookup, one set of filters.

That is what lets everything in [the execution pipeline](/guide/execution-pipeline) apply to a
queue handler: validation, timeouts, the error mapping, your own filters. A trigger handler is not
a second kind of thing with a second set of rules.

## Binding

The parameter a handler declares is what the payload binds to. Services come from the container the
way they do [everywhere else](/guide/parameter-binding); what is left is the payload.

```csharp
[Queue("orders")]
public async Task OnOrder(Order order, IOrderStore store) => await store.Place(order);
```

A `[Timer]` handler usually takes no parameter at all, because a schedule carries nothing but the
fact that it fired. That is the ordinary case there rather than an oddity.

```csharp
[Timer("nightly-rollup")]
public void Nightly() => _reports.Roll();
```

`[Blob]` is the exception worth knowing. **The notification is the message, and the object is not in
it.** A store sends a bucket, a key, a size and an event name; fetching the object is a call the
handler makes for itself. That is the transport's design rather than the framework's, and it is why
a blob handler binds metadata where a queue handler binds a payload.

## Batches, and what a failure means

Several sources deliver a batch. The runtime unpacks it and calls the handler once per item, so a
handler never sees the batch it arrived in. What differs is what happens after one item fails, and
it differs by transport rather than by preference.

Reporting is off until the deployment says otherwise. `ReportBatchItemFailures` on the adapter
module has to match the event source mapping:

```csharp
[HardenedModule]
[SqsModule(ReportBatchItemFailures = true)]
public partial class Application;
```

It has to match because reporting failures to a mapping that did not ask for them means the report
is discarded and every failed message is marked handled — the opposite of what was intended. With
reporting off, the first failure fails the invocation, which is the only thing that makes any
transport redeliver.

With reporting on, `BatchFailureMode` decides the shape:

| Mode | Source | Behaviour |
|---|---|---|
| `PerItem` | A queue | Every item is attempted. The report names exactly what to redeliver |
| `Checkpoint` | A shard | The run stops at the first failure. Everything from that item onward is redelivered |

SQS and Kinesis are both answered with the same `batchItemFailures` array and mean different things
by it: SQS deletes everything not named, and Kinesis rewinds to the earliest name and redelivers
from there. Running the same loop against both is correct for one of them.

`Checkpoint` also preserves the ordering a shard exists to give. A stream promises items in order
per partition key, and handling item five after item three failed breaks that promise on the retry,
where three is replayed after five has already been applied.

::: warning Ordered sources need idempotent handlers
`[Change]` and `[Stream]` rewind. An item that succeeded before a later one failed is delivered
again on the retry. Write those handlers so a second delivery is not a second write.
:::

## Two adapters, one trigger

`[Change]` and `[Stream]` are both ordered and both deliver a batch, and they are still two
triggers rather than one. A change feed carries a row before and after an edit, so a handler binds
an image of the item and can ask what the edit was. A stream carries records a publisher wrote,
which are opaque until the handler decodes them. Same delivery shape, different vocabularies — and
one adapter package serves each, because two packages binding a single property would resolve
first-import-wins on an import order nobody controls.

## When nothing serves a trigger

A handler whose trigger no referenced package declares would compile, deploy, and never be invoked.
The build refuses instead:

```
HRDF001  Handlers in this project use [Queue], but no referenced runtime declares a
         module for it. Reference a runtime package that supports queue triggers, or
         set <HardenedQueueModule> to the module that serves them.
```

The reverse is a warning. `HRDF003` names an adapter bound to serve a trigger nothing in the
project declares, because it ships in the deployment bundle unreachable. See
[Diagnostics](/reference/diagnostics).

## Testing

Each trigger kind gets a generated façade on the entry point — `Queues`, `Topics`, `Timers`,
`Changes`, `Streams`, `Blobs`, `Invocations` — with a method per source. A test takes the façade it
wants as a parameter and sends through it:

```csharp
[HardenedTest]
public async Task AMessageReachesTheHandler(Application.Queues queues, OrderLog log) {
    await queues.Orders(new Order { Id = "A-1" });

    Assert.Equal("A-1", Assert.Single(log.Orders).Id);
}
```

The method exists because the handler does, and its parameter is the type the handler binds, so a
renamed source or a changed payload is a compile error in the test rather than a test that passes
against nothing. Passing several payloads sends a batch.

`[assembly: FunctionTesting]` is what makes the façades resolvable. Adding a provider's own testing
attribute beside it raises the fidelity without changing a single test method; see
[Testing AWS handlers](/aws/testing).

## Next

- [AWS](/aws/): the adapter packages, and the Lambda host
- [Parameter binding](/guide/parameter-binding): how the payload and services are bound
- [The execution pipeline](/guide/execution-pipeline): the filters a trigger handler runs through

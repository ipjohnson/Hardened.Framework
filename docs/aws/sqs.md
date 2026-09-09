# Queues and topics

A queue handler receives one message at a time. The runtime unpacks the batch and runs each message
through its own execution chain, so the handler never sees the batch it arrived in.

```csharp
using Hardened.Functions.Runtime.Attributes;

public class OrderHandlers {

    [Queue("orders-new")]
    public void OnOrder(Order order, IOrderStore store) => store.Place(order);
}
```

Nothing above names SQS, and the application module does not either:

```csharp
[HardenedModule]
public partial class Application;
```

Referencing `Hardened.Aws.Lambda.Sqs` is what decides that `[Queue]` means SQS. See
[Triggers](/guide/triggers) for the vocabulary, and the six other sources it covers.

## Packages

```xml
<PackageReference Include="Hardened.Aws.Lambda.Runtime" Version="0.32.0-rc1000" />
<PackageReference Include="Hardened.Aws.Lambda.Sqs" Version="0.32.0-rc1000" />
```

`Hardened.Aws.Lambda.Sns` serves `[Topic]` the same way. A topic fans out — every subscriber sees
every message, and nothing is competing for it — but the handler is written identically:

```csharp
[Topic("order-events")]
public void OnOrderEvent(OrderEvent published) => _projections.Apply(published);
```

`dotnet new hardened-function --trigger queue` and `--trigger topic` write each shape with tests.

## What a failure means

By default, throwing fails the invocation. That is what returns the messages to the queue, and with
no failure report there is no other way to say "not handled" — so the whole batch is redelivered,
including the messages that succeeded.

Reporting individual failures is a deployment decision, not a code one, and the application has to
say so:

```csharp
using Hardened.Aws.Lambda.Sqs;

[HardenedModule]
[SqsModule(ReportBatchItemFailures = true)]
public partial class Application;
```

This is the one place a queue application writes the module out, because `ReportBatchItemFailures`
is not a property of the code at all. It is whether the event source mapping was deployed with
`ReportBatchItemFailures` turned on, and the code has no way to know.

::: danger Getting it wrong in this direction loses messages
A report sent to a mapping that did not ask for one is discarded, and the whole batch is marked
successful — the failed messages are deleted rather than redelivered. That is why it is off by
default and why turning it on takes a deliberate line.
:::

With reporting on, the adapter answers with a `batchItemFailures` array naming exactly the messages
that threw. SQS deletes everything not named, so only the failures come back.

A queue's failure mode is `PerItem`: every message is attempted, because stopping early would leave
the rest unattempted and unreported, which the transport reads as handled. That differs from a
stream, where a failure rewinds; see [Streams and change feeds](/aws/ddb-streams).

## Testing

The generated façade sends through the real pipeline. Passing several payloads sends a batch:

```csharp
[HardenedTest]
public async Task EveryMessageInABatchIsHandled(Application.Queues queues, OrderLog log) {
    await queues.OrdersNew(
        new Order { Id = "A-1" }, new Order { Id = "A-2" }, new Order { Id = "A-3" });

    Assert.Equal(3, log.Orders.Count);
}
```

The method name is the queue's, so `[Queue("orders-new")]` is `queues.OrdersNew`. It exists because
the handler does, and its parameter is the type the handler binds — a renamed queue or a changed
payload is a compile error here rather than a test that passes against nothing.

Adding `[assembly: LambdaTesting]` packs each message into the SQS envelope AWS actually sends and
goes in through the invocation loop, so the adapter and the batch failure report are exercised too.
Each message is given an id of the queue name and its position in the batch — `orders-new-0`,
`orders-new-1` — so a reported failure can be traced back to the message that caused it. No test
method changes. See [Testing AWS handlers](/aws/testing).

## Next

- [Triggers](/guide/triggers): the batch and failure semantics in full
- [Streams and change feeds](/aws/ddb-streams): the ordered sources, which fail differently
- [Testing AWS handlers](/aws/testing): the two fidelity levels

# SQS

An SQS handler receives one message at a time. The runtime unpacks the batch, runs each message
through its own execution chain, and returns a partial batch response naming exactly the messages
that failed.

```csharp
using Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;
using Hardened.Amz.Function.Sqs.Runtime;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Shared.Runtime.Attributes;

[HardenedModule]
[LambdaFunctionModule]
[SqsLambda]
public partial class Application { }

public class OrderConsumer {
    [HardenedFunction("consume-orders")]
    public async Task Consume(OrderMessage message, IOrderService orders) {
        await orders.Process(message.OrderId);
    }
}
```

```csharp
[HardenedTest]
public async Task ReportsOnlyTheBadMessage(TestSqsApp sqs) {
    var response = await sqs.SendMessage(
        new OrderMessage { OrderId = "good" },
        new OrderMessage { OrderId = null },      // fails
        new OrderMessage { OrderId = "also-good" });

    var failure = Assert.Single(response.BatchItemFailures);

    Assert.Equal("1", failure.ItemIdentifier);
}
```

The message body is deserialized into the parameter, so the handler works in terms of your type
rather than an `SQSMessage`. `dotnet new hardened-function --trigger sqs` writes this shape.
Source: [`src/Lambda/Sqs`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/Sqs)
in [Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz).

## The envelope

For the message attributes, the receipt handle or the raw body, take `ISqsMessageContext`:

```csharp
public interface ISqsMessageContext {
    SQSEvent? SqsEvent { get; set; }
    SQSEvent.SQSMessage? Message { get; set; }
}
```

```csharp
public class OrderConsumer(ISqsMessageContext context) {

    [HardenedFunction("consume-orders")]
    public async Task Consume(OrderMessage message, IOrderService orders) {
        var attempt = context.Message!.Attributes["ApproximateReceiveCount"];

        await orders.Process(message.OrderId, int.Parse(attempt));
    }
}
```

## Partial batch responses

Each message runs on a forked chain with its own request and response. The runtime collects the
outcomes and writes an `SQSBatchResponse` whose `BatchItemFailures` name the failed messages by
`MessageId`. Report nothing and one poison message redelivers the whole batch forever; report
everything and messages that succeeded are processed twice.

A message fails when its chain ends with a status of 300 or above, or when the handler throws. A
handler that catches its own exception and returns normally is reporting success.

::: warning Enable it on the trigger too
Partial batch responses only take effect when the event source mapping is configured with
`ReportBatchItemFailures`. Without it, AWS ignores the response and redelivers the entire batch.
:::

## Handling errors

Two seams, at different levels. `ISqsExceptionHandler` sees the message and decides whether the
exception is handled:

```csharp
public interface ISqsExceptionHandler {
    ValueTask<bool> HandleException(
        IExecutionChain chain, SQSEvent.SQSMessage message, Exception exception);
}
```

The default logs the exception and returns `false`, so the message is reported as failed and
redelivered. Returning `true` marks it handled.

`IBatchProcessorExceptionHandler` is the lower-level hook shared with the
[DynamoDB stream runtime](/aws/ddb-streams#which-records-failed), for behaviour that should apply
to any batch source.

## Testing

```csharp
[assembly: LambdaFunctionTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]
```

`TestSqsApp.SendMessage` serializes each message, runs the batch, and returns the real response.
Messages are given `MessageId`s from their position in the array, so a failure is traceable back
to the message that caused it. Assert the identifier rather than the count: the correct number of
failures reported against the wrong messages redelivers those and deletes the real poison
message.

## Next

- [DynamoDB Streams](/aws/ddb-streams): the same batch shape over stream records
- [Testing AWS handlers](/aws/testing): the harnesses in full
- [The execution pipeline](/guide/execution-pipeline#forking): how a chain runs per record

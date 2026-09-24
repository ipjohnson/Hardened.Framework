# Queues

`[Queue("orders")]` on a method makes it the handler for messages from the Amazon SQS queue named
`orders`. Lambda invokes the function with a batch of messages, and the handler runs once for each
message, in the batch's order.

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Queue("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

In the template, `Order` has a string `Id` and an int `Quantity`. `OrderLog` is a
`[SingletonService]` that keeps the orders it is given.

The application class names no module:

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
public partial class Application;
```

The package reference to `Hardened.Aws.Lambda.Sqs` makes `[Queue]` mean SQS. The build registers
the package's `SqsModule` on the application. [Triggers](/guide/triggers) covers `[Queue]` and the
other trigger attributes.

## Packages

A queue function references `Hardened.Aws.Lambda.Sqs` beside `Hardened.Aws.Lambda.Runtime`:

```xml
<PackageReference Include="Hardened.Aws.Lambda.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Aws.Lambda.Sqs" Version="0.0.0-HARDENED-VERSION" />
```

The other packages, `Program.cs` and the project settings are the same for every Lambda function.
The AWS [Overview](/aws/) covers them.

This command writes the function above and a test project:

```bash
dotnet new hardened-function -n Orders --trigger queue
```

`--host aws` is the template's default.

## Queue names

The SQS adapter takes the queue's name from the last segment of the batch's `eventSourceARN`. A
batch from `arn:aws:sqs:us-east-1:123456789012:orders` reaches `[Queue("orders")]`, under the route
`QUEUE /orders`. The adapter reads the route once for the batch, from the first record. Every
message in the batch goes to the same handler.

The name in `[Queue]` matches the queue's name exactly, including case. A FIFO queue's name ends in
`.fifo`. The attribute names a FIFO queue in full, as in `[Queue("orders.fifo")]`.

One function can serve several queues, with a handler and an event source mapping for each. A batch
from a queue that no handler names fails the invocation with `InvalidOperationException`. For a
queue named `payments`, the exception's message is "No handler is registered for QUEUE /payments.
An event source is wired to this function that no trigger attribute declared."

## Body and headers

The message's `body` is the request body, as UTF-8 bytes. [Triggers](/guide/triggers) covers how
the body binds to the handler's parameter.

A message attribute whose `dataType` is `String` or `Number` becomes a header named for the
attribute. The message id, the receipt handle and the queue's ARN become the three headers in this
table:

| From the SQS record | Reaches the handler as |
|---|---|
| `body` | The request body |
| `messageAttributes` with `dataType` `String` or `Number` | A header named for the attribute |
| `messageAttributes` with `dataType` `Binary` | Nothing. The whole invocation fails |
| `messageId` | `x-amz-sqs-message-id` |
| `receiptHandle` | `x-amz-sqs-receipt-handle` |
| `eventSourceARN` | `x-amz-sqs-queue-arn`, and the route |
| `attributes`: `ApproximateReceiveCount`, `SentTimestamp`, `MessageGroupId` and the rest | Nothing |

The adapter writes the three `x-amz-sqs-` headers after the attribute headers, so an attribute with
the same name does not replace them. A header lookup ignores the case of the name. SQS assigns the
message id when the message is sent. The receipt handle is different every time the message is
received.

A `Binary` attribute on any message fails the whole invocation before any handler runs. The
exception is a `JsonException` whose message begins "The JSON value could not be converted to
System.IO.MemoryStream." Lambda returns every message of that batch to the queue.

A handler reads the headers through an `IExecutionRequest` parameter. The interface is in the
`Hardened.Requests.Abstract.Execution` namespace. This handler reads two headers:

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
        request.Headers.TryGetValue("x-amz-sqs-message-id", out var messageId);
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

A header can be missing, so the handler reads each one with `TryGetValue`.

This event has the shape of the example in the AWS documentation:

```json
{
  "Records": [
    {
      "messageId": "059f36b4-87a3-44ab-83d2-661975830a7d",
      "receiptHandle": "AQEBwJnKyrHigUMZj6rYigCgxlaS3SLy0a...",
      "body": "{\"id\":\"A-1\",\"quantity\":2}",
      "attributes": {
        "ApproximateReceiveCount": "1",
        "SentTimestamp": "1545082649183",
        "SenderId": "AIDAIENQZJOLO23YVJ4VO",
        "ApproximateFirstReceiveTimestamp": "1545082649185"
      },
      "messageAttributes": {
        "tenant": {
          "stringValue": "acme",
          "stringListValues": [],
          "binaryListValues": [],
          "dataType": "String"
        }
      },
      "md5OfBody": "e4e68fb7bd0e697a0ae8f1bb342846b3",
      "eventSource": "aws:sqs",
      "eventSourceARN": "arn:aws:sqs:us-east-1:123456789012:orders",
      "awsRegion": "us-east-1"
    }
  ]
}
```

For this event, the handler above logs
`Message 059f36b4-87a3-44ab-83d2-661975830a7d for tenant acme`. The AWS [Overview](/aws/) covers
running the function locally and sending it an event.

### Messages from an SNS topic

A queue subscribed to an SNS topic receives the SNS notification's JSON as the message body, unless
the subscription has raw message delivery turned on. A handler that binds `Order` from that
notification's JSON gets an `Order` with default values, and no error.
`--attributes RawMessageDelivery=true` on `aws sns subscribe` turns raw message delivery on:

```bash
aws sns subscribe --protocol sqs \
    --topic-arn arn:aws:sns:us-east-1:123456789012:orders \
    --notification-endpoint arn:aws:sqs:us-east-1:123456789012:orders \
    --attributes RawMessageDelivery=true
```

## Failures and redelivery

A message fails when its handler throws, when its body does not bind, or when it fails validation.
An invocation that does not fail answers with a `batchItemFailures` report. Without
`ReportBatchItemFailures`, the report is always empty: `{"batchItemFailures":[]}`.

| Case | `ReportBatchItemFailures` off | `ReportBatchItemFailures` on |
|---|---|---|
| A handler throws, or a body fails binding or validation | The invocation fails, the later messages do not run, and Lambda returns the whole batch | The invocation succeeds, every message runs, and Lambda returns only the messages the report names |
| Every message fails | The invocation fails at the first message | The invocation succeeds, and the report names every message |
| No handler names the queue | The invocation fails | The invocation fails |
| A message has a `Binary` attribute | The invocation fails before any handler runs | The invocation fails before any handler runs |

By default, the first failed message fails the whole invocation with its exception. The messages
after it in the batch do not run. Lambda then returns every message of the batch to the queue,
including the messages that ran. Lambda delivers them again after the queue's visibility timeout.

With `ReportBatchItemFailures` on, a failed message does not fail the invocation. Every message in
the batch runs. The report names each failed message by its `messageId`. This is the function's
answer to a batch of two whose second message failed:

```json
{"batchItemFailures":[{"itemIdentifier":"2e1424d4-f796-459a-8184-9c92662be6da"}]}
```

Lambda deletes the messages that the report does not name. It returns the named messages to the
queue.

The messages of one invocation run one after another. They share the invocation's cancellation
token. The function's timeout has to cover the whole batch. The AWS [Overview](/aws/) covers when
that token is cancelled.

On a FIFO queue with reporting on, a failed message does not stop the batch. The later messages of
the same message group run. The report names only the failed message. Lambda then deletes those
later messages. It delivers the failed message again after them, so the function handles the
group's messages out of order. With reporting off, the first failure in a FIFO batch fails the
invocation. Lambda returns the whole batch, so the group keeps its order.

The queue's redrive policy moves a message to the dead-letter queue after the message has been
received `maxReceiveCount` times. Lambda can deliver a message more than once, even when no
invocation failed.

## Reporting failed messages

`[SqsModule(ReportBatchItemFailures = true)]` on the application class turns reporting on. The
module is in the `Hardened.Aws.Lambda.Sqs` namespace. `ReportBatchItemFailures` is its only
setting:

| Property | Default | When `true` |
|---|---|---|
| `ReportBatchItemFailures` | `false` | A failed message does not fail the invocation. Every message runs, and the function answers with the ids of the failed messages |

```csharp
using Hardened.Aws.Lambda.Sqs;
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
[SqsModule(ReportBatchItemFailures = true)]
public partial class Application;
```

With the attribute on the application, the build registers only the module that the application
declares.

The setting has to match `ReportBatchItemFailures` on the queue's event source mapping. With
`ReportBatchItemFailures` on the mapping and off in the application, the invocation fails at the
first failed message. Lambda then returns the whole batch to the queue.

::: warning
With `[SqsModule(ReportBatchItemFailures = true)]` and an event source mapping without
`ReportBatchItemFailures`, Lambda ignores the report. Lambda deletes every message of an invocation
that did not fail, including the messages that failed. Turn the setting on in both places or in
neither.
:::

The AWS documentation covers
[the event source mapping](https://docs.aws.amazon.com/lambda/latest/dg/with-sqs.html) and
[reporting batch item failures](https://docs.aws.amazon.com/lambda/latest/dg/services-sqs-errorhandling.html).

## Connecting the queue

An event source mapping connects a queue to the function. The queue's name is the name in
`[Queue]`. This command creates a mapping from the `orders` queue to the deployed function
`Orders`:

```bash
aws lambda create-event-source-mapping \
    --function-name Orders \
    --event-source-arn arn:aws:sqs:us-east-1:123456789012:orders \
    --function-response-types ReportBatchItemFailures
```

`--function-response-types ReportBatchItemFailures` turns reporting on in the mapping. It matches
`[SqsModule(ReportBatchItemFailures = true)]`. Leave the option out when the application leaves the
setting off.

The mapping's batch size sets how many messages one invocation carries. It can be up to 10,000 for
a standard queue, and up to 10 for a FIFO queue. The function's execution role needs the
permissions in the AWS managed policy `AWSLambdaSQSQueueExecutionRole` to read the queue. The AWS
[Overview](/aws/) covers deploying the function, which is the same for every trigger.

## Testing

The template's test project declares `[assembly: LambdaTesting]`. The attribute delivers each
façade call as one SQS event, with a record for each message:

| Record field | Value |
|---|---|
| `messageId` | The queue's name and the message's position: `orders-0`, `orders-1` |
| `receiptHandle` | `receipt-0`, `receipt-1` |
| `eventSourceARN` | `arn:aws:sqs:us-east-1:123456789012:orders` |
| `messageAttributes` | None |

Under `[FunctionTesting]` alone, the request has no headers. The handler above that reads two
headers passes its tests under both.

[Testing functions](/guide/testing-functions) covers what a failed message does to the façade call,
for each delivery and each `ReportBatchItemFailures` setting. The façade call does not return the
`batchItemFailures` report. The AWS [Testing](/aws/testing) page covers reading it.

## Next

| Page | Covers |
|---|---|
| [Triggers](/guide/triggers) | The trigger attributes, source names, payload binding and `BatchFailureMode` |
| [Topics](/aws/topic) | SNS notifications |
| [Overview](/aws/) | The packages, `Program.cs`, running locally and deploying a function |
| [Testing](/aws/testing) | What `[LambdaTesting]` builds, and reading the batch report |
| [Testing functions](/guide/testing-functions) | Testing a trigger handler |

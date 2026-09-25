# Topics

`[Topic("orders")]` on a method makes it the handler for notifications published to the Amazon SNS topic named `orders`. Each notification is one call to the handler.

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Topic("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

In the template, `Order` has a string `Id` and an int `Quantity`. `OrderLog` is a `[SingletonService]` that keeps the orders it is given.

The application class names no module:

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
public partial class Application;
```

A reference to `Hardened.Aws.Lambda.Sns` makes `[Topic]` mean SNS. The build registers the package's `SnsModule` on the application. The module has no settings. An application does not need to declare it.

[Triggers](/guide/triggers) covers `[Topic]` and the other trigger attributes.

## Packages

A topic function references `Hardened.Aws.Lambda.Sns` beside `Hardened.Aws.Lambda.Runtime`:

```xml
<PackageReference Include="Hardened.Aws.Lambda.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Aws.Lambda.Sns" Version="0.0.0-HARDENED-VERSION" />
```

The other packages, `Program.cs` and the project settings are the same for every Lambda function. The AWS [Overview](/aws/) covers them.

This command writes the function above and a test project:

```bash
dotnet new hardened-function -n Orders --trigger topic
```

`--host aws` is the template's default.

## Routing

The adapter takes the topic's name from the last segment of the notification's `TopicArn`. A notification whose `TopicArn` is `arn:aws:sns:us-east-1:123456789012:orders` reaches `[Topic("orders")]` under the route `TOPIC /orders`. The topic's name and the name in `[Topic]` must match exactly, including case.

A notification from a topic that no handler names fails the invocation with `InvalidOperationException`. For a topic named `payments`, the message is:

```text
No handler is registered for TOPIC /payments. An event source is wired to this function that no trigger attribute declared.
```

A function can subscribe to a standard topic only, not to a FIFO topic.

One function can serve a topic and a queue. `[Topic("orders")]` and `[Queue("orders")]` are two routes, `TOPIC /orders` and `QUEUE /orders`. Each event reaches its own handler. [Queues](/aws/queue) covers SQS.

## Body and headers

Each invocation carries one notification. The notification's `Message` is the request body, as UTF-8 bytes. The rest of the SNS envelope is not in the body. [Triggers](/guide/triggers) covers how the body binds to the handler's parameter.

The fields of an SNS record reach the handler as follows:

| From the SNS record | Reaches the handler as |
|---|---|
| `Sns.Message` | The request body |
| `Sns.MessageAttributes` with `Type` `String` | A header named for the attribute |
| `Sns.MessageAttributes` with `Type` `Number`, `String.Array` or `Binary` | Nothing |
| `Sns.MessageId` | `x-amz-sns-message-id` |
| `Sns.Subject` | `x-amz-sns-subject`, when the publisher set a subject |
| `Sns.TopicArn` | `x-amz-sns-topic-arn`, and the route |
| `Sns.Timestamp`, `EventSubscriptionArn` and the signature fields | Nothing |

`x-amz-sns-message-id` and `x-amz-sns-topic-arn` replace a message attribute with the same name. `x-amz-sns-subject` replaces a message attribute with the same name only when the notification has a subject. Header names are matched without regard to case.

A handler reads the headers through a parameter of type `IExecutionRequest`. The interface is in `Hardened.Requests.Abstract.Execution`. A header can be missing. A notification can have no subject. A test under `[FunctionTesting]` alone sends no headers ([Testing](#testing)). This handler reads each header with `TryGetValue`:

```csharp
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Topic("orders")]
    public void OnOrder(Order order, IExecutionRequest request)
    {
        request.Headers.TryGetValue("x-amz-sns-message-id", out var messageId);
        request.Headers.TryGetValue("x-amz-sns-subject", out var subject);

        logger.LogInformation(
            "Notification {MessageId}: {Subject}",
            messageId.ToString(),
            subject.ToString()
        );

        log.Record(order);
    }
}
```

This event follows the shape of the example in AWS's documentation:

```json
{
  "Records": [
    {
      "EventVersion": "1.0",
      "EventSubscriptionArn": "arn:aws:sns:us-east-1:123456789012:orders:21be56ed-a058-49f5-8c98-aedd2564c486",
      "EventSource": "aws:sns",
      "Sns": {
        "SignatureVersion": "1",
        "Timestamp": "2026-09-23T12:45:07.000Z",
        "Signature": "tcc6faL2yUC6dgZdmrwh1Y4cGa/ebXEkAi6RibDsvpi+tE/1+82j...65r==",
        "SigningCertURL": "https://sns.us-east-1.amazonaws.com/SimpleNotificationService-ac565b8b1a6c5d002d285f9598aa1d9b.pem",
        "MessageId": "95df01b4-ee98-5cb9-9903-4c221d41eb5e",
        "Message": "{\"id\":\"A-1\",\"quantity\":2}",
        "MessageAttributes": {
          "tenant": {
            "Type": "String",
            "Value": "acme"
          }
        },
        "Type": "Notification",
        "UnsubscribeUrl": "https://sns.us-east-1.amazonaws.com/?Action=Unsubscribe&SubscriptionArn=arn:aws:sns:us-east-1:123456789012:orders:21be56ed-a058-49f5-8c98-aedd2564c486",
        "TopicArn": "arn:aws:sns:us-east-1:123456789012:orders",
        "Subject": "Order placed"
      }
    }
  ]
}
```

Sent to the function running locally, this event makes the handler log `Notification 95df01b4-ee98-5cb9-9903-4c221d41eb5e: Order placed`. The AWS [Overview](/aws/) covers running the function locally and sending it an event.

## Failures and retries

A notification fails when its handler throws or its body does not bind. The invocation then fails with that exception. An invocation that does not fail answers with an empty body.

SNS reads no answer from the function. The function has no way to report which notifications failed. `SnsModule` has no `ReportBatchItemFailures`.

SNS invokes the function asynchronously. When an asynchronous invocation fails, Lambda runs it two more times by default. The second attempt runs one minute after the first. The third runs two minutes after the second. After the last attempt, Lambda discards the notification, unless the function has a dead-letter queue or an on-failure destination.

A dead-letter queue on the topic's subscription holds only notifications that SNS could not deliver to Lambda, such as after the function was deleted.

::: warning
A function with no dead-letter queue and no on-failure destination loses a notification whose handler fails three times. A dead-letter queue on the subscription does not catch it.
:::

Lambda can run the handler more than once for one notification, even when no invocation failed.

AWS's documentation covers asynchronous invocation, its retries and the two dead-letter queues:

| AWS page | Covers |
|---|---|
| [Invoking Lambda functions with Amazon SNS notifications](https://docs.aws.amazon.com/lambda/latest/dg/with-sns.html) | Asynchronous invocation by SNS |
| [How Lambda handles errors and retries with asynchronous invocation](https://docs.aws.amazon.com/lambda/latest/dg/invocation-async-error-handling.html) | The retries, and the function's dead-letter queue |
| [Amazon SNS dead-letter queues](https://docs.aws.amazon.com/sns/latest/dg/sns-dead-letter-queues.html) | The subscription's dead-letter queue |

## Subscribing the function to the topic

Two steps connect the topic to the function: a permission that lets SNS invoke the function, and a subscription of the function to the topic. The topic's name is the name in `[Topic]` ([Routing](#routing)). These AWS CLI commands take both steps:

```bash
aws lambda add-permission --function-name Orders \
    --source-arn arn:aws:sns:us-east-1:123456789012:orders \
    --statement-id orders-topic --action lambda:InvokeFunction \
    --principal sns.amazonaws.com

aws sns subscribe --protocol lambda \
    --topic-arn arn:aws:sns:us-east-1:123456789012:orders \
    --notification-endpoint arn:aws:lambda:us-east-1:123456789012:function:Orders
```

`Orders` is the deployed function's name. `orders-topic` is a statement id that you choose.

Lambda's retries, the function's dead-letter queue and its on-failure destination are settings of the function's asynchronous invocation ([Failures and retries](#failures-and-retries)). Deploying the function itself is the same for every trigger. The AWS [Overview](/aws/) covers it.

## Testing

For `--trigger topic`, the template writes this test. It sends a notification through the `Application.Topics` façade:

```csharp
using DependencyModules.xUnit.Attributes;
using Xunit;

namespace Orders.Tests;

public class OrderHandlerTests
{
    [ModuleTest]
    public async Task ANotificationReachesTheHandler(Application.Topics topics, OrderLog log)
    {
        await topics.Orders(new Order { Id = "A-1", Quantity = 2 });

        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }
}
```

The template's test project declares `[assembly: LambdaTesting]`. The attribute delivers each façade call as one SNS event, with a record per message. A call with several messages builds one event with several records. SNS never sends such an event. Each record's message id is the topic's name and the message's position: `orders-0`, `orders-1`. Its `TopicArn` is `arn:aws:sns:us-east-1:123456789012:orders`. It has no subject and no message attributes.

Under `[FunctionTesting]` alone, the request has no headers. The handler in [Body and headers](#body-and-headers) passes its tests under both.

A failed notification fails the façade call with the handler's exception, under `[LambdaTesting]` and under `[FunctionTesting]` alone. The messages after it in the same call do not run.

[Testing functions](/guide/testing-functions) covers the façades and the deliveries.

## Next

| Page | Covers |
|---|---|
| [Queues](/aws/queue) | SQS, including a queue subscribed to a topic |
| [Triggers](/guide/triggers) | The trigger attributes, source names and payload binding |
| [Overview](/aws/) | The packages, `Program.cs`, running locally and deploying a function |
| [Testing functions](/guide/testing-functions) | Testing a trigger handler |

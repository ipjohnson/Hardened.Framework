# Events

`[Event("com.acme.orders", "OrderPlaced")]` on a method makes it the handler for Amazon EventBridge
events whose `source` is `com.acme.orders` and whose `detail-type` is `OrderPlaced`. The handler's
parameter binds the event's `detail`.

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Event("com.acme.orders", "OrderPlaced")]
    public void OnOrderPlaced(Order order) => log.Record(order);
}
```

In the `hardened-function` template, `Order` has a string `Id` and an int `Quantity`. `OrderLog` is
a `[SingletonService]` that keeps the orders it is given.

Each event is one invocation of the function. The handler runs once in each invocation.
[Triggers](/guide/triggers) covers `[Event]` and its two arguments.

The template's application class names no module:

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
public partial class Application;
```

The package reference to `Hardened.Aws.Lambda.EventBridge` makes `[Event]` mean EventBridge. The
build registers the package's `EventBridgeModule` on the application. The module has no settings.
The same package serves `[Timer]`, which [Timers](/aws/timer) covers.

## Packages

A function with `[Event]` handlers references `Hardened.Aws.Lambda.EventBridge` beside
`Hardened.Aws.Lambda.Runtime`:

```xml
<PackageReference Include="Hardened.Aws.Lambda.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Aws.Lambda.EventBridge" Version="0.0.0-HARDENED-VERSION" />
```

The other packages, `Program.cs` and the project settings are the same for every Lambda function.
The AWS [Overview](/aws/) covers them.

`dotnet new hardened-function` has no `--trigger` value for events. `--trigger timer` writes a
function that already references `Hardened.Aws.Lambda.EventBridge`.

## Routing

The adapter in `Hardened.Aws.Lambda.EventBridge` routes an event on its `source` and its
`detail-type`, as `EVENT /{source}/{detail-type}`. The route of the handler in the example is
`EVENT /com.acme.orders/OrderPlaced`. The `source` and the `detail-type` must match exactly,
including case. One function can serve several events, with a handler for each.

An event that no handler declares fails the invocation with `InvalidOperationException`. For an
`OrderShipped` event from `com.acme.orders`, the message is "No handler is registered for EVENT
/com.acme.orders/OrderShipped. An event source is wired to this function that no trigger attribute
declared."

An AWS service's events route the same way. A handler with `[Event("aws.s3", "Object Created")]`
receives Amazon S3's Object Created events. The `source` of an AWS service's event begins with
`aws.`. The `source` of an application's own event cannot begin with `aws.`. [Blobs](/aws/blob)
covers S3's own object notifications, through `[Blob]`.

An event whose `source` is `aws.events` and whose `detail-type` is `Scheduled Event` goes to
`[Timer]` handlers. [Timers](/aws/timer) covers it.

A `source` or `detail-type` that contains a character such as `:` or `&` fails the build with
`HardenedException`. `CS0234` follows, for the handler class that the generator did not write. For
`[Event("com.acme.orders", "order:placed")]`, `dotnet build` reports:

```text
error HardenedException: The generator threw and produced no source: ArgumentException: The hintName 'EVENT.com.acme.orders/order:placed.FunctionHandler.cs' contains an invalid character ':' at position 27. (Parameter 'hintName')
```

A `source` or `detail-type` with spaces, dots, hyphens, underscores or parentheses builds and
routes.

## Body and headers

The event's `detail` is the request body, as compact JSON. [Triggers](/guide/triggers) covers how
the body binds to the handler's parameter.

The event's `id`, `source`, `detail-type` and `time` become four headers:

| From the EventBridge event | Reaches the handler as |
|---|---|
| `detail` | The request body |
| `id` | `x-amz-event-id` |
| `source` | `x-amz-event-source`, and the route |
| `detail-type` | `x-amz-event-detail-type`, and the route |
| `time` | `x-amz-event-time` |
| `resources`, `account`, `region`, `version`, `replay-name` | Nothing |

Header names are matched without regard to case. EventBridge generates the `id` for every event.

A handler reads the headers through an `IExecutionRequest` parameter, from
`Hardened.Requests.Abstract.Execution`:

```csharp
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Event("com.acme.orders", "OrderPlaced")]
    public void OnOrderPlaced(Order order, IExecutionRequest request)
    {
        request.Headers.TryGetValue("x-amz-event-id", out var eventId);
        request.Headers.TryGetValue("x-amz-event-time", out var time);

        logger.LogInformation("Event {EventId} at {Time}", eventId.ToString(), time.ToString());

        log.Record(order);
    }
}
```

A header can be missing. An event that a test sends through `ITriggerDelivery` carries none. The
example reads each header with `TryGetValue`.

An EventBridge event for this handler:

```json
{
  "version": "0",
  "id": "6a7e8feb-b491-4cf7-a9f1-bf3703467718",
  "detail-type": "OrderPlaced",
  "source": "com.acme.orders",
  "account": "123456789012",
  "time": "2026-09-23T12:45:07Z",
  "region": "us-east-1",
  "resources": [],
  "detail": {
    "id": "A-1",
    "quantity": 2
  }
}
```

For this event, the handler logs `Event 6a7e8feb-b491-4cf7-a9f1-bf3703467718 at 2026-09-23T12:45:07Z`.
The AWS [Overview](/aws/) covers running the function locally and sending it an event.

## Failures and retries

An event fails when its handler throws, or when its `detail` does not bind. The invocation then
fails with that exception. An invocation that does not fail answers with an empty body.

EventBridge invokes the function asynchronously. Lambda queues the event. Lambda answers EventBridge
with a success before the function runs. When the invocation fails, Lambda runs it two more times by
default: one minute after the first attempt, then two minutes after the second. After the last
attempt, Lambda discards the event, unless the function has a dead-letter queue or an on-failure
destination.

The retry policy of a rule's target and its dead-letter queue cover events that EventBridge could
not deliver to Lambda, such as when a permission is missing. The retry policy defaults to 24 hours
and 185 attempts. An event whose handler failed reaches neither of them.

::: warning
A function with no dead-letter queue and no on-failure destination loses an event whose handler
fails three times. A dead-letter queue on the rule's target does not catch it.
:::

A rule can send one event to its target more than once. Lambda can also run the handler more than
once for one event, even when no invocation failed.

AWS's documentation covers
[asynchronous invocation and its retries](https://docs.aws.amazon.com/lambda/latest/dg/invocation-async-error-handling.html),
[a rule target's retry policy](https://docs.aws.amazon.com/eventbridge/latest/userguide/eb-rule-retry-policy.html)
and
[a rule target's dead-letter queue](https://docs.aws.amazon.com/eventbridge/latest/userguide/eb-rule-dlq.html).

## Deploying

A rule on an event bus sends each event that matches its event pattern to its targets. Three
commands connect the function. They create a rule with an event pattern, let EventBridge invoke the
function, and make the function the rule's target:

```bash
aws events put-rule --name order-placed \
    --event-pattern '{"source": ["com.acme.orders"], "detail-type": ["OrderPlaced"]}'

aws lambda add-permission --function-name Orders \
    --statement-id order-placed --action lambda:InvokeFunction \
    --principal events.amazonaws.com \
    --source-arn arn:aws:events:us-east-1:123456789012:rule/order-placed

aws events put-targets --rule order-placed \
    --targets Id=orders,Arn=arn:aws:lambda:us-east-1:123456789012:function:Orders
```

`Orders` is the deployed function's name. The statement id `order-placed` is a name of your
choosing.

The pattern matches the `source` and `detail-type` that the handler declares. An event that the
pattern lets through and no handler declares fails the invocation.

AWS services send their events to the default event bus. On another bus, `put-rule` and
`put-targets` take `--event-bus-name`. The rule's ARN then has the form
`arn:aws:events:{region}:{account}:rule/{bus}/{rule}`.

The target sends the whole event by default. When a target sends part of the event, a constant or
an input transformer's output, the payload does not reach the handler. The invocation fails.

`aws events put-events` publishes an event. The entry's `Source`, `DetailType` and `Detail` become
the event's `source`, `detail-type` and `detail`. `Detail` is a JSON object written as a string:

```bash
aws events put-events --entries \
    '[{"Source": "com.acme.orders", "DetailType": "OrderPlaced", "Detail": "{\"id\": \"A-1\", \"quantity\": 2}"}]'
```

An entry that lacks `Detail`, `DetailType` or `Source` fails. Without `EventBusName`, the event goes
to the default bus.

Deploying the function itself is the same for every trigger. The AWS [Overview](/aws/) covers it.
AWS's documentation covers
[rules and event patterns](https://docs.aws.amazon.com/eventbridge/latest/userguide/eb-create-rule-visual.html),
[resource-based policies](https://docs.aws.amazon.com/eventbridge/latest/userguide/eb-use-resource-based.html)
and
[publishing events](https://docs.aws.amazon.com/eventbridge/latest/APIReference/API_PutEventsRequestEntry.html).

## Testing

A test sends the EventBridge event itself through `LambdaInvocationHandler`, from
`Hardened.Aws.Lambda.Runtime.Hosting`. The test resolves the invocation handler from its
`IServiceProvider` parameter. It calls `Invoke` with the event's JSON and an `ILambdaContext`, from
`Amazon.Lambda.Core`:

```csharp
using System.Text;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orders.Tests;

public class OrderHandlerTests
{
    [HardenedTest]
    public async Task AnEventReachesTheHandler(IServiceProvider provider, OrderLog log)
    {
        var json = """
            {
              "version": "0",
              "id": "6a7e8feb-b491-4cf7-a9f1-bf3703467718",
              "detail-type": "OrderPlaced",
              "source": "com.acme.orders",
              "account": "123456789012",
              "time": "2026-09-23T12:45:07Z",
              "region": "us-east-1",
              "resources": [],
              "detail": { "id": "A-1", "quantity": 2 }
            }
            """;

        var handler = provider.GetRequiredService<LambdaInvocationHandler>();

        await handler.Invoke(new MemoryStream(Encoding.UTF8.GetBytes(json)), new Context());

        Assert.Equal("A-1", Assert.Single(log.Orders).Id);
    }

    private sealed class Context : ILambdaContext
    {
        public string AwsRequestId => "test";
        public IClientContext ClientContext => null!;
        public string FunctionName => "Orders";
        public string FunctionVersion => "$LATEST";
        public ICognitoIdentity Identity => null!;
        public string InvokedFunctionArn => "arn:aws:lambda:us-east-1:123456789012:function:Orders";
        public ILambdaLogger Logger => null!;
        public string LogGroupName => "/aws/lambda/Orders";
        public string LogStreamName => "test";
        public int MemoryLimitInMB => 512;
        public TimeSpan RemainingTime => TimeSpan.FromSeconds(30);
    }
}
```

The test passes under `[LambdaTesting]` and under `[FunctionTesting]` alone. The event goes through
the adapter, so the handler receives the four headers. When the `[Event]` handler throws, `Invoke`
throws the handler's exception. The AWS [Testing](/aws/testing) page covers the invocation handler
in tests.

`[Event]` has no trigger façade. [Testing functions](/guide/testing-functions) covers sending an
event through `ITriggerDelivery`. In a Lambda function, an `[Event]` sent that way has these
results:

| Test | Result |
|---|---|
| Under `[LambdaTesting]` | Sending fails with `NotSupportedException`. `[LambdaTesting]` builds no EventBridge event |
| Without `[LambdaTesting]`, in a function whose only adapter package is `Hardened.Aws.Lambda.EventBridge` | An `[Event]` handler that takes a payload does not run. The payload is refused with `JsonException`. The call returns without an error |
| Without `[LambdaTesting]`, in a function that also references a package such as `Hardened.Aws.Lambda.Sqs` | The event reaches the handler with no headers |

The message of the `NotSupportedException` is:

```text
No test envelope is built for the EVENT scheme yet. Queues, topics, timers, changes, streams and blobs have one; events are addressed by source and detail type and need their own shape.
```

## Next

| Page | Covers |
|---|---|
| [Timers](/aws/timer) | EventBridge schedules, from the same package |
| [Triggers](/guide/triggers) | The trigger attributes and payload binding |
| [Overview](/aws/) | The packages, `Program.cs`, running locally and deploying a function |
| [Testing functions](/guide/testing-functions) | Testing a trigger handler |
| [Testing](/aws/testing) | Testing on AWS |

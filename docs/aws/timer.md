# Timers

`[Timer("nightly")]` on a method makes it the handler for the Amazon EventBridge schedule named `nightly`. Each scheduled event is one invocation of the function, and the handler runs once for it.

```csharp
using Hardened.Functions.Runtime.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [Timer("nightly")]
    public void OnNightly() => log.Sweep();
}
```

In the `hardened-function` template, `OrderLog` is a `[SingletonService]` that counts the runs in `Sweeps`. `Sweep()` adds one. The template's `Application` class names no module:

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace Orders;

[HardenedModule]
public partial class Application;
```

The project's reference to `Hardened.Aws.Lambda.EventBridge` makes `[Timer]` mean EventBridge. The build registers the package's `EventBridgeModule` on the application, so an application does not need to declare it. The module has no settings. The same package serves `[Event]`, which [Events](/aws/event) covers. [Triggers](/guide/triggers) covers `[Timer]` and the other trigger attributes.

## Packages and the template

A timer function references `Hardened.Aws.Lambda.EventBridge` beside `Hardened.Aws.Lambda.Runtime`:

```xml
<PackageReference Include="Hardened.Aws.Lambda.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Aws.Lambda.EventBridge" Version="0.0.0-HARDENED-VERSION" />
```

The other packages, `Program.cs` and the project settings are the same for every Lambda function. The AWS [Overview](/aws/) covers them.

This command writes the function above and a test project:

```bash
dotnet new hardened-function -n Orders --trigger timer
```

`--host aws` is the template's default.

## Which schedules reach the handler

`EventBridgeAdapter` in `Hardened.Aws.Lambda.EventBridge` treats an event as a scheduled run when its `source` is `aws.events` and its `detail-type` is `Scheduled Event`. The adapter takes the schedule's name from the last segment of the first ARN in the event's `resources`. An event whose first ARN is `arn:aws:events:us-east-1:123456789012:rule/nightly` reaches `[Timer("nightly")]`, under the route `TIMER /nightly`. `[Timer]` matches the name exactly, including case.

An EventBridge scheduled rule sends an event with those two values, and the rule's own ARN in `resources`. Scheduled rules can be created only on the default event bus. AWS's documentation calls scheduled rules a legacy feature. It recommends EventBridge Scheduler.

An EventBridge Scheduler schedule sends its target's input as the event. A schedule reaches the handler when its input is a scheduled event, with `aws.events` in `source`, `Scheduled Event` in `detail-type` and the schedule's ARN in `resources`. A schedule's ARN ends with its group and its name, as in `arn:aws:scheduler:us-east-1:123456789012:schedule/default/nightly`, so its last segment is the schedule's name. [Creating a rule or a schedule](#creating-a-rule-or-a-schedule) shows the input.

| What fires | Reaches `[Timer("nightly")]` |
|---|---|
| An EventBridge scheduled rule named `nightly` | Yes |
| An EventBridge Scheduler schedule named `nightly`, with no input | No. The invocation fails |
| An EventBridge Scheduler schedule named `nightly`, whose input is a scheduled event | Yes |
| An EventBridge Scheduler schedule with an input of another shape, such as AWS's example `{ "Payload": "TEST_PAYLOAD" }` | No. The invocation fails with `No handler is registered for EVENT //` |

An event from a schedule that no handler names fails the invocation with `InvalidOperationException`:

```text
No handler is registered for TIMER /weekly. An event source is wired to this function that no trigger attribute declared.
```

In a function that also references another adapter package, such as `Hardened.Aws.Lambda.Sqs`, an input without `source` and `detail-type` fails the invocation with this message:

```text
No adapter recognised this payload. The function is built for EventBridgeAdapter, SqsAdapter, so either an event source is wired to it that no handler asked for, or a handler's trigger has no adapter registered.
```

One function can serve several schedules, with a handler for each. Every other EventBridge event routes to `[Event]` handlers. [Events](/aws/event) covers them. One function can hold `[Timer]` and `[Event]` handlers together.

## What the handler receives

The event's `detail` becomes the request body, and its `id`, `source`, `detail-type` and `time` become four headers:

| From the EventBridge event | Reaches the handler as |
|---|---|
| `detail` | The request body, `{}` for a scheduled rule |
| `id` | `x-amz-event-id` |
| `source` | `x-amz-event-source`, `aws.events` for a schedule |
| `detail-type` | `x-amz-event-detail-type`, `Scheduled Event` for a schedule |
| `time` | `x-amz-event-time` |
| `resources` | The route, from the last segment of its first ARN |
| `version`, `account`, `region` | Nothing |

A field that is missing or empty sets no header. Header names match without regard to case. A `[Timer]` handler usually takes no payload parameter. [Triggers](/guide/triggers) covers payload binding.

A handler reads the headers through an `IExecutionRequest` parameter. The interface is in the namespace `Hardened.Requests.Abstract.Execution`. A header can be missing, so this handler reads its two headers with `TryGetValue`:

```csharp
using Hardened.Functions.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging;

namespace Orders;

public class OrderHandler(OrderLog log, ILogger<OrderHandler> logger)
{
    [Timer("nightly")]
    public void OnNightly(IExecutionRequest request)
    {
        request.Headers.TryGetValue("x-amz-event-id", out var eventId);
        request.Headers.TryGetValue("x-amz-event-time", out var time);

        logger.LogInformation("Run {EventId} at {Time}", eventId.ToString(), time.ToString());

        log.Sweep();
    }
}
```

This event from a scheduled rule follows the example in the AWS documentation:

```json
{
  "version": "0",
  "id": "89d1a02d-5ec7-412e-82f5-13505f849b41",
  "detail-type": "Scheduled Event",
  "source": "aws.events",
  "account": "123456789012",
  "time": "2026-09-23T02:00:00Z",
  "region": "us-east-1",
  "resources": ["arn:aws:events:us-east-1:123456789012:rule/nightly"],
  "detail": {}
}
```

The AWS [Overview](/aws/) covers running the function locally and sending it an event.

## Failures and retries

A run fails when its handler throws. The invocation then fails with that exception. An invocation that does not fail answers with an empty body.

EventBridge rules and EventBridge Scheduler invoke the function asynchronously. Lambda queues the event and answers the rule or the schedule with a success before the function runs. When the invocation fails, Lambda runs it two more times by default, one minute after the first attempt and then two minutes after the second.

The retry policy and dead-letter queue of a rule's target cover events that EventBridge could not deliver to Lambda, such as when a permission is missing. The retry policy and dead-letter queue of a schedule cover invocations that EventBridge Scheduler could not make.

::: warning
When all three attempts fail, Lambda discards the event, unless the function has a dead-letter queue or an on-failure destination. A run whose handler failed reaches neither the retry policy nor the dead-letter queue of the rule's target or of the schedule.
:::

A rule can run more than once for one scheduled time, and Lambda can run the handler more than once for one event, even when no invocation failed.

AWS's documentation covers [asynchronous invocation and its retries](https://docs.aws.amazon.com/lambda/latest/dg/invocation-async-error-handling.html), [the rule target's retries and dead-letter queue](https://docs.aws.amazon.com/eventbridge/latest/userguide/eb-rule-dlq.html) and [the schedule's dead-letter queue](https://docs.aws.amazon.com/scheduler/latest/UserGuide/configuring-schedule-dlq.html).

## Creating a rule or a schedule

A scheduled rule fires the timer, and so does an EventBridge Scheduler schedule with the input below. Give the rule or the schedule the name in `[Timer]`. The AWS [Overview](/aws/) covers deploying the function itself, which is the same for every trigger.

### A scheduled rule

Three steps connect a scheduled rule to the function: the rule with a schedule expression, a permission that lets EventBridge invoke the function, and the function as the rule's target. In these commands, `Orders` is the deployed function's name. `nightly-rule` is a statement id of your choosing.

```bash
aws events put-rule --name nightly --schedule-expression "cron(0 2 * * ? *)"

aws lambda add-permission --function-name Orders \
    --statement-id nightly-rule --action lambda:InvokeFunction \
    --principal events.amazonaws.com \
    --source-arn arn:aws:events:us-east-1:123456789012:rule/nightly

aws events put-targets --rule nightly \
    --targets Id=orders,Arn=arn:aws:lambda:us-east-1:123456789012:function:Orders
```

A scheduled rule's expression is in UTC. The target sends the whole event by default. A target that sends part of the event, a constant or an input transformer's output does not reach the handler, and the invocation fails.

### An EventBridge Scheduler schedule

A schedule reaches the handler with the input in this `target.json`, which `aws scheduler create-schedule` reads. In the file, `orders-scheduler` is the schedule's execution role.

```json
{
  "Arn": "arn:aws:lambda:us-east-1:123456789012:function:Orders",
  "RoleArn": "arn:aws:iam::123456789012:role/orders-scheduler",
  "Input": "{\"source\": \"aws.events\", \"detail-type\": \"Scheduled Event\", \"id\": \"<aws.scheduler.execution-id>\", \"time\": \"<aws.scheduler.scheduled-time>\", \"resources\": [\"<aws.scheduler.schedule-arn>\"], \"detail\": {}}"
}
```

The input is a JSON document written as a string. EventBridge Scheduler replaces three context attributes in it:

| Context attribute | Replaced with | Reaches the handler as |
|---|---|---|
| `<aws.scheduler.schedule-arn>` | The schedule's ARN | The route |
| `<aws.scheduler.scheduled-time>` | The time the schedule was due | `x-amz-event-time` |
| `<aws.scheduler.execution-id>` | An id for the invocation | `x-amz-event-id` |

The schedule's execution role needs permission for `lambda:InvokeFunction` on the function. `create-schedule` requires `--flexible-time-window`. `Mode=OFF` invokes the function at the scheduled time.

```bash
aws scheduler create-schedule --name nightly \
    --schedule-expression "cron(0 2 * * ? *)" \
    --flexible-time-window Mode=OFF \
    --target file://target.json
```

For a run due at 02:00 UTC on 2026-09-23, the function receives this event from the schedule. The `id` is AWS's example execution id.

```json
{"source": "aws.events", "detail-type": "Scheduled Event", "id": "d32c5kddcf5bb8c3", "time": "2026-09-23T02:00:00Z", "resources": ["arn:aws:scheduler:us-east-1:123456789012:schedule/default/nightly"], "detail": {}}
```

AWS's documentation covers [scheduled rules](https://docs.aws.amazon.com/eventbridge/latest/userguide/eb-create-rule-schedule.html), [EventBridge Scheduler's targets](https://docs.aws.amazon.com/scheduler/latest/UserGuide/managing-targets-templated.html) and [its context attributes](https://docs.aws.amazon.com/scheduler/latest/UserGuide/managing-schedule-context-attributes.html).

## Testing

`dotnet new hardened-function --trigger timer` writes a test that runs the handler through `Application.Timers`. [Testing functions](/guide/testing-functions) shows the test and covers the façades.

The template's test project declares `[assembly: LambdaTesting]`. The attribute delivers each call to a timer as the event of a scheduled rule named after the timer. For `[Timer("nightly")]`, the event has these fields:

| Field | Value |
|---|---|
| `id` | `nightly-fired`, the timer's name followed by `-fired` |
| `time` | `2026-01-01T00:00:00Z` |
| `resources` | `arn:aws:events:us-east-1:123456789012:rule/nightly` |
| `detail` | `{}` |

Under `[FunctionTesting]` alone, the request has no headers and an empty body. The handler in [What the handler receives](#what-the-handler-receives) passes the template's test under both. Under `[LambdaTesting]`, a call whose handler throws fails with the handler's exception.

The AWS [Testing](/aws/testing) page covers what `[LambdaTesting]` builds for each trigger.

## Next

| Page | Covers |
|---|---|
| [Events](/aws/event) | EventBridge events, from the same package |
| [Triggers](/guide/triggers) | The trigger attributes, source names and payload binding |
| [Overview](/aws/) | The packages, `Program.cs`, running locally and deploying a function |
| [Testing functions](/guide/testing-functions) | Testing a trigger handler |
| [Testing](/aws/testing) | What `[LambdaTesting]` builds |

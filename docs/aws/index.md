# AWS

The handlers you wrote for Kestrel run on Lambda. What changes is a package reference.

```csharp
using Hardened.Shared.Runtime.Attributes;

[HardenedModule]
public partial class Application;

public class OrderHandler(OrderLog log) {

    [Queue("orders")]
    public void OnOrder(Order order) => log.Record(order);
}
```

**There is no host module attribute, and that is the point.** The handler carries a
[trigger](/guide/triggers); the generator reads the build property the adapter package declares and
registers the module for you. Nothing in the application names a cloud, so moving to another one is
a package reference.

`dotnet new hardened-function` and `dotnet new hardened-web --host aws-lambda` write both shapes
with tests; see [Project templates](/guide/project-templates).

## The packages

One host package, and one adapter per source. An adapter is a package rather than a flag so a
function carries only the event models it can reach: a queue function never has the API Gateway or
stream models in its deployment bundle.

| Package | Serves |
|---|---|
| `Hardened.Aws.Lambda.Runtime` | The host. The invocation loop, the bootstrap, logging and metrics. Every Lambda application references it |
| `Hardened.Aws.Lambda.Http` | `[Get]`, `[Post]`, `[Put]`, `[Patch]`, `[Delete]`, behind an API Gateway HTTP API or a function URL |
| `Hardened.Aws.Lambda.Invoke` | `[HardenedFunction]`, a direct invocation |
| `Hardened.Aws.Lambda.Sqs` | `[Queue]` |
| `Hardened.Aws.Lambda.Sns` | `[Topic]` |
| `Hardened.Aws.Lambda.EventBridge` | `[Timer]` and `[Event]` |
| `Hardened.Aws.Lambda.DynamoDb` | `[Change]` |
| `Hardened.Aws.Lambda.Kinesis` | `[Stream]` |
| `Hardened.Aws.Lambda.S3` | `[Blob]` |
| `Hardened.Aws.Lambda.Testing` | `[LambdaTesting]` and `[LambdaWebTesting]` |

Beside them, and not a host or an adapter: `Hardened.Aws.DynamoDbClient` supplies DynamoDB clients
to an application on any host, and `Hardened.Aws.DynamoDbClient.Testing` stands a real DynamoDB up
in a container for a test. See [DynamoDB client](/aws/dynamodb).

Each adapter is a `[DependencyModule]` and nothing references it until an application applies one,
so the linker finds nothing rooting an adapter a project does not use. A function that handles no
queue carries no SQS adapter, no `SqsSerializerContext`, and no `Amazon.Lambda.SQSEvents.dll`.

## What the runtime gives you

**Structured CloudWatch logging.** The runtime replaces `ILoggerProvider` with one that writes
structured lines CloudWatch Logs Insights can query, with the request id attached.

**Embedded metrics.** `IMetricLogger` writes the CloudWatch Embedded Metric Format, so metrics come
out of the log stream without an extra API call.

**Batch delivery.** The SQS, SNS, DynamoDB, Kinesis and S3 adapters unpack the batch and call the
handler once per item. Whether a failure is reported per item is
[`ReportBatchItemFailures`](/guide/triggers#batches-and-what-a-failure-means), and it has to match
the event source mapping.

**The log level from the environment.** `Information`, or `Debug` in `development` and `test`,
overridden by `LOG_LEVEL`. See [Log level](/guide/environments#log-level).

## Cold start

Routing tables, service registrations and parameter binding are all emitted during the build, so a
cold start does no assembly scanning and no reflection over your types. On its first invocation the
process constructs a service provider from a list of registrations and calls a method.

The container is built before the invocation loop starts, on purpose: a missing registration is a
cold start that fails immediately naming what was missing, rather than the first invocation of the
day failing while every later one on a warm sandbox succeeds.

The runtimes are compatible with trimming and Native AOT — every registration is a literal
`typeof()` the trimmer can follow.

## Where things are

| Area | Page | Source |
|---|---|---|
| HTTP | [Web applications](/aws/lambda-web) | [`Hardened.Aws.Lambda.Http`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Aws/Hardened.Aws.Lambda.Http) |
| The host, and direct invocation | [Lambda functions](/aws/lambda-function) | [`Hardened.Aws.Lambda.Runtime`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Aws/Hardened.Aws.Lambda.Runtime) |
| Queues and topics | [Queues](/aws/sqs) | [`Hardened.Aws.Lambda.Sqs`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Aws/Hardened.Aws.Lambda.Sqs) |
| Change feeds and streams | [Streams](/aws/ddb-streams) | [`Hardened.Aws.Lambda.DynamoDb`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Aws/Hardened.Aws.Lambda.DynamoDb) |
| Test harnesses | [Testing AWS handlers](/aws/testing) | [`Hardened.Aws.Lambda.Testing`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Aws/Hardened.Aws.Lambda.Testing) |
| DynamoDB clients | [DynamoDB client](/aws/dynamodb) | [`Hardened.Aws.DynamoDbClient`](https://github.com/ipjohnson/Hardened.Framework/tree/main/src/Clouds/Aws/Hardened.Aws.DynamoDbClient) |

The trigger attributes themselves are not here. They live in `Hardened.Functions.Runtime`, which
names no cloud; see [Triggers](/guide/triggers).

::: info The Hardened.Amz line
The AWS packages were `Hardened.Amz.*` until `0.22.0-rc1000`, in a repository of their own. That
line has stopped and is not being renamed: these packages replace it, on the framework's own
version line, and the module attributes and test harnesses are different. An application on
`Hardened.Amz` moves by changing its package references and its entry point class, not by a
find-and-replace. The [DynamoDB client](/aws/dynamodb) is the exception — it was not a host, so it
came across unchanged but for its namespace.
:::

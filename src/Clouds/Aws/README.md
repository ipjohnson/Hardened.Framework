# ![Hardened](https://raw.githubusercontent.com/ipjohnson/Hardened.Amz/main/assets/hardened-mark-32.png) Hardened.Amz

Runs [Hardened](https://ipjohnson.github.io/Hardened.Docs) applications on AWS Lambda. The handlers,
parameter binding, configuration and tests are the core framework's. This repository supplies what
runs underneath: the Lambda runtimes, response streaming, the test harnesses, a DynamoDB client and
CDK constructs.

AWS documentation: **[ipjohnson.github.io/Hardened.Docs/aws](https://ipjohnson.github.io/Hardened.Docs/aws/)**

## Start here

The [Framework templates](https://github.com/ipjohnson/Hardened.Framework#start-here) generate
Lambda projects directly:

```bash
dotnet new install Hardened.Templates
dotnet new hardened-web -n Todos --host aws-lambda        # web API behind API Gateway
dotnet new hardened-function -n OrderQueue --trigger sqs  # SQS batch processor
```

The first generates the same todo API the Kestrel host does, and a harness that runs it locally
over HTTP. A Lambda application is an ordinary Hardened application with a runtime module on it,
and the module is the only line that differs from the Kestrel bootstrap:

```csharp
[HardenedModule]
[LambdaWebModule]         // [KestrelRuntime] in the Kestrel host
[TodosLibrary]
public partial class Application;
```

No handler, service or test in the library project knows which of the two it is running under.
That is the whole migration.

The function template is a different shape: a handler marked `[HardenedFunction]` rather than a
route, invoked with the message body bound to its parameter.

```csharp
[HardenedModule]
[LambdaFunctionModule]
[SqsLambda]
public partial class Application;

public class OrderHandler(OrderLog log) {
    [HardenedFunction]
    public Task Process(Order order) {
        log.Record(order);

        return Task.CompletedTask;
    }
}
```

The runtime module is not optional. An application without one compiles, then throws
`'Application' is missing [LambdaFunctionModule]` the moment it is constructed.
[Lambda application types](https://github.com/ipjohnson/Hardened.Amz/blob/main/docs/application-types.md)
covers the project shape, handler and test setup for each transport.

## Pick your trigger

| You are handling | Module | Runtime package |
|---|---|---|
| [HTTP behind API Gateway or a function URL](https://ipjohnson.github.io/Hardened.Docs/aws/lambda-web) | `[LambdaWebModule]` | `Hardened.Amz.Web.Lambda.Runtime` |
| [Direct invocation](https://ipjohnson.github.io/Hardened.Docs/aws/lambda-function) | `[LambdaFunctionModule]` | `Hardened.Amz.Function.Lambda.Runtime` |
| [SQS batches](https://ipjohnson.github.io/Hardened.Docs/aws/sqs) | `[LambdaFunctionModule]` + `[SqsLambda]` | `Hardened.Amz.Function.Sqs.Runtime` |
| [DynamoDB Streams](https://ipjohnson.github.io/Hardened.Docs/aws/ddb-streams) | `[LambdaFunctionModule]` + `[DynamoStreamLambda]` | `Hardened.Amz.Function.DDB.Runtime` |

SQS and DynamoDB Streams take a second attribute because they are event sources layered on the
direct-invoke path. The SQS runtime deserialises each message body into your handler's parameter
type and reports partial batch failures, so a throwing handler fails one message rather than the
batch.

Every application runs on `Amazon.Lambda.RuntimeSupport`'s bootstrap through a generated `Main`.
Response streaming is a deployment setting rather than a separate host: with
`HARDENED_LAMBDA_RESPONSE_MODE=stream` behind a function URL in `RESPONSE_STREAM` invoke mode,
every response opens a stream at its first body byte, and a handler returning
`IAsyncEnumerable<T>` writes one chunk per item. `Hardened.Amz.Cdk` sets the variable and the
invoke mode together; see
[Response mode](https://github.com/ipjohnson/Hardened.Amz/blob/main/docs/application-types.md#response-mode).

## Testing without AWS

Each runtime has a matching harness that drives the real pipeline in-process. No deployed function,
and no mocked SDK types:

- `LambdaTestApp` (`Hardened.Amz.Function.Lambda.Testing`) invokes function handlers.
- `TestSqsApp` (`Hardened.Amz.Function.Sqs.Testing`) delivers batches and asserts partial failures.
- `Hardened.Amz.Function.DDB.Testing` feeds stream records.
- `[LocalDynamoDb]` (`Hardened.Amz.DynamoDbClient.Testing`) runs DynamoDB in Testcontainers.
- `Hardened.Amz.Web.Lambda.Harness` puts the API Gateway pipeline behind a local HTTP listener,
  streaming when the application is deployed in stream mode.

See [testing AWS handlers](https://ipjohnson.github.io/Hardened.Docs/aws/testing) and
[testing conventions](https://github.com/ipjohnson/Hardened.Amz/blob/main/docs/testing-conventions.md).

## Clients and infrastructure

`Hardened.Amz.DynamoDbClient` provides `IDynamoDbClientProvider` and DynamoDB extensions
([docs](https://ipjohnson.github.io/Hardened.Docs/aws/dynamodb)). There is no SQS client package —
the SQS runtime consumes a queue; writing to one means taking a direct `AWSSDK.SQS` dependency.
`Hardened.Amz.Cdk` carries the CDK constructs
([docs](https://ipjohnson.github.io/Hardened.Docs/aws/cdk)).

All packages ship to nuget.org as `Hardened.Amz.*`, releasing in step with the Framework's version
line. The full list is in the
[package reference](https://ipjohnson.github.io/Hardened.Docs/reference/packages).

## Building from source

```bash
dotnet build Hardened.Amz.sln
dotnet test  Hardened.Amz.sln
```

`src/Directory.Build.targets` prefers a sibling `../Hardened.Framework` checkout over the pinned
packages when one exists, so the two repositories can be edited together. A checkout at an
incompatible commit fails the build with errors in *the other repository's* files. Force either side
explicitly:

```bash
dotnet build Hardened.Amz.sln -p:UseLocalHardenedFramework=false   # pinned packages, as CI builds
dotnet build Hardened.Amz.sln -p:UseLocalHardenedFramework=true    # sibling checkout
```

CI adds `-p:ContinuousIntegrationBuild=true`, which turns warnings into errors — run a build with
it set before opening a pull request. The DynamoDB client tests need a running Docker daemon, and
fail rather than skip without one — see [testing conventions](https://github.com/ipjohnson/Hardened.Amz/blob/main/docs/testing-conventions.md).

## Related repositories

- [Hardened.Framework](https://github.com/ipjohnson/Hardened.Framework) — the core framework: contracts, routing, DI, testing
- [Hardened.Docs](https://github.com/ipjohnson/Hardened.Docs) — the documentation site

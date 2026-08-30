# <picture><source media="(prefers-color-scheme: dark)" srcset="assets/hardened-mark-dark.svg"><img src="assets/hardened-mark.svg" alt="" width="34"></picture> Hardened.Amz

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
dotnet new hardened-web -n Orders --host aws-lambda      # web API behind API Gateway
dotnet new hardened-function -n OrderQueue --trigger sqs # SQS batch processor
```

A Lambda application is an ordinary Hardened application with a runtime module on it. That is all
that changes, so the same handlers run on Kestrel locally and API Gateway deployed:

```csharp
[HardenedModule]
[LambdaWebModule]
public partial class Application { }

public class ProductController {
    [Get("/api/products/{id}")]
    public Product GetProduct(string id) => new() { Id = id, Name = "Widget" };
}
```

The runtime module is not optional. An application without one compiles, then throws
`'Application' is missing [LambdaFunctionModule]` the moment it is constructed.
[Lambda application types](docs/application-types.md) covers the project shape, handler and test
setup for each transport.

## Pick your trigger

| You are handling | Module | Runtime package |
|---|---|---|
| [HTTP behind API Gateway](https://ipjohnson.github.io/Hardened.Docs/aws/lambda-web) | `[LambdaWebModule]` | `Hardened.Amz.Web.Lambda.Runtime` |
| [Direct invocation](https://ipjohnson.github.io/Hardened.Docs/aws/lambda-function) | `[LambdaFunctionModule]` | `Hardened.Amz.Function.Lambda.Runtime` |
| [SQS batches](https://ipjohnson.github.io/Hardened.Docs/aws/sqs) | `[LambdaFunctionModule]` + `[SqsLambda]` | `Hardened.Amz.Function.Sqs.Runtime` |
| [DynamoDB Streams](https://ipjohnson.github.io/Hardened.Docs/aws/ddb-streams) | `[LambdaFunctionModule]` + `[DynamoStreamLambda]` | `Hardened.Amz.Function.DDB.Runtime` |
| Streaming web responses | `[StreamingLambdaWebModule]` | `Hardened.Amz.Web.Lambda.Streaming` |
| Streaming function responses | `[StreamingLambdaFunctionModule]` | `Hardened.Amz.Function.Lambda.Streaming` |

SQS and DynamoDB Streams take a second attribute because they are event sources layered on the
direct-invoke path. The SQS runtime deserialises each message body into your handler's parameter
type and reports partial batch failures, so a throwing handler fails one message rather than the
batch.

The streaming modules drive the Lambda Runtime API directly and start writing the response as it is
produced. The function must be deployed with the `RESPONSE_STREAM` invoke mode, which
`Hardened.Amz.Cdk` does not configure for you today.

## Testing without AWS

Each runtime has a matching harness that drives the real pipeline in-process. No deployed function,
and no mocked SDK types:

- `LambdaTestApp` (`Hardened.Amz.Function.Lambda.Testing`) invokes function handlers.
- `TestSqsApp` (`Hardened.Amz.Function.Sqs.Testing`) delivers batches and asserts partial failures.
- `Hardened.Amz.Function.DDB.Testing` feeds stream records.
- `[LocalDynamoDb]` (`Hardened.Amz.DynamoDbClient.Testing`) runs DynamoDB in Testcontainers.
- `Hardened.Amz.Web.Lambda.Harness` puts the API Gateway pipeline behind a local HTTP listener.

See [testing AWS handlers](https://ipjohnson.github.io/Hardened.Docs/aws/testing) and
[testing conventions](docs/testing-conventions.md).

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
fail rather than skip without one — see [testing conventions](docs/testing-conventions.md).

## Related repositories

- [Hardened.Framework](https://github.com/ipjohnson/Hardened.Framework) — the core framework: contracts, routing, DI, testing
- [Hardened.Docs](https://github.com/ipjohnson/Hardened.Docs) — the documentation site

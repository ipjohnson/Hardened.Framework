# Hardened.Amz

AWS integrations for the [Hardened](https://ipjohnson.github.io/Hardened.Docs) ecosystem — Lambda
runtimes for functions, web APIs, DynamoDB Streams and SQS; response streaming for functions and
web APIs; a DynamoDB client library; and CDK constructs.

## Packages

### Lambda Runtimes

| Package | Description |
|---|---|
| `Hardened.Amz.Function.Lambda.Runtime` | Lambda function runtime |
| `Hardened.Amz.Function.Lambda.Testing` | `LambdaTestApp` for testing Lambda functions |
| `Hardened.Amz.Function.Lambda.SourceGenerator` | Lambda function source generator |
| `Hardened.Amz.Web.Lambda.Runtime` | Lambda web runtime (API Gateway) |
| `Hardened.Amz.Web.Lambda.SourceGenerator` | Lambda web source generator |
| `Hardened.Amz.Function.DDB.Runtime` | DynamoDB Streams Lambda runtime |
| `Hardened.Amz.Function.DDB.Testing` | DynamoDB Streams testing utilities |
| `Hardened.Amz.Function.Sqs.Runtime` | SQS batch processing Lambda runtime |
| `Hardened.Amz.Function.Sqs.Testing` | `TestSqsApp` for testing SQS processors |
| `Hardened.Amz.Shared.Lambda.Runtime` | Shared Lambda utilities |
| `Hardened.Amz.Shared.Lambda.Testing` | Shared Lambda testing utilities |

### Response Streaming

Both packages drive the Lambda Runtime API directly and write the
`application/vnd.awslambda.http-integration-response` format — a JSON prelude of status and
headers, then the body as it is produced. The function must be deployed with the `RESPONSE_STREAM`
invoke mode; `Hardened.Amz.Cdk` does not configure that for you today.

| Package | Description |
|---|---|
| `Hardened.Amz.Function.Lambda.Streaming` | Response streaming for Lambda functions |
| `Hardened.Amz.Web.Lambda.Streaming` | Response streaming for Lambda web applications |

### Local Hosting

| Package | Description |
|---|---|
| `Hardened.Amz.Web.Lambda.Harness` | Runs the Lambda web pipeline behind an HTTP listener, so a request can be driven end to end without deploying |

### Client Libraries

| Package | Description |
|---|---|
| `Hardened.Amz.DynamoDbClient` | `IDynamoDbClientProvider`, DynamoDB extensions |
| `Hardened.Amz.DynamoDbClient.Testing` | `[LocalDynamoDb]` with Testcontainers |

There is no SQS client package. `Hardened.Amz.Function.Sqs.Runtime` consumes a queue; writing to
one means taking a direct `AWSSDK.SQS` dependency for now.

### Infrastructure

| Package | Description |
|---|---|
| `Hardened.Amz.Cdk` | AWS CDK constructs |

## Quick Start

### Lambda Function

`[LambdaFunctionModule]` brings the Lambda invocation path and, through the `[HardenedRequestModule]`
it carries, the request pipeline. As with `[LambdaWebModule]` below, it is not optional: an
application without it compiles, and then throws `'Application' is missing [LambdaFunctionModule]`
the moment it is constructed.

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;

[HardenedModule]
[LambdaFunctionModule]
public partial class Application { }

public class OrderHandler {
    [HardenedFunction("process-order")]
    public OrderResponse ProcessOrder(OrderRequest request) {
        return new OrderResponse { OrderId = Guid.NewGuid().ToString() };
    }
}
```

### Lambda Web API (API Gateway)

`[LambdaWebModule]` brings the API Gateway host and the web pipeline underneath it. An application
without it builds and then throws on construction, so it is not optional.

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Amz.Web.Lambda.Runtime.DependencyInjection;

[HardenedModule]
[LambdaWebModule]
public partial class Application { }

public class ProductController {
    [Get("/api/products/{id}")]
    public Product GetProduct(string id) {
        return new Product { Id = id, Name = "Widget" };
    }
}
```

### Lambda Web API with response streaming

`[StreamingLambdaWebModule]` replaces the buffered API Gateway host with the streaming one. The
source generator emits a different bootstrap for it, so it is the attribute, not a configuration
flag, that selects the transport.

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Amz.Web.Lambda.Streaming;

[HardenedModule]
[StreamingLambdaWebModule]
public partial class Application { }
```

The function-side equivalent is `[StreamingLambdaFunctionModule]` from
`Hardened.Amz.Function.Lambda.Streaming`.

Applying the module is the whole opt-in: it selects the streaming bootstrap *and* registers the
streaming runtime. There is one attribute per transport, and it goes on the application class
itself — an attribute on a member or a nested type does not select anything.

`[StreamingLambdaWebApplication]` was a second name the generator accepted that registered nothing,
so an application using it compiled and then threw on construction. It is now a build error
pointing at the module. `[StreamingLambdaFunction]` was renamed to `[StreamingLambdaFunctionModule]`
at the same time, for consistency with every other module.

### SQS batch processing

`[SqsLambda]` adds SQS batch handling on top of `[LambdaFunctionModule]`; both are needed. A
handler takes the deserialised message body, and the runtime reports partial batch failures back
to SQS so only the failed messages are redelivered.

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;
using Hardened.Amz.Function.Sqs.Runtime;

[HardenedModule]
[LambdaFunctionModule]
[SqsLambda]
public partial class Application { }

public class OrderQueueHandler {
    [HardenedFunction]
    public Task Process(OrderRequest order) {
        // throwing marks this one message failed; the rest of the batch still succeeds
        return Task.CompletedTask;
    }
}
```

## Building

```bash
dotnet build Hardened.Amz.sln
dotnet test Hardened.Amz.sln
```

`src/Directory.Build.targets` prefers a sibling `../Hardened.Framework` checkout over the pinned
packages when one exists, so that the two repositories can be edited together with no flag to
remember. The cost is that a checkout at an incompatible commit fails the build with errors in
*the other repository's* files, which does not look like a configuration problem. Force either
side explicitly:

```bash
dotnet build Hardened.Amz.sln -p:UseLocalHardenedFramework=false   # pinned packages, as CI builds
dotnet build Hardened.Amz.sln -p:UseLocalHardenedFramework=true    # sibling checkout
dotnet build Hardened.Amz.sln -p:HardenedFrameworkRoot=/path/to/checkout
```

CI additionally sets `-p:ContinuousIntegrationBuild=true`, which turns warnings into errors. Local
builds do not, so an in-progress edit is not blocked by an unused variable. Run a build with it set
before opening a pull request.

The DynamoDB client tests use Testcontainers and need a running Docker daemon. They fail rather
than skip without one, deliberately — see [docs/testing-conventions.md](docs/testing-conventions.md).

## Documentation

In this repository:

- **[Lambda application types](docs/application-types.md)** — the module attribute, project shape,
  handler and test setup for each transport, and what the common construction-time failures mean.
- [Testing conventions](docs/testing-conventions.md)

Full documentation is available at **[ipjohnson.github.io/Hardened.Docs](https://ipjohnson.github.io/Hardened.Docs)**.

- [AWS Overview](https://ipjohnson.github.io/Hardened.Docs/aws/overview/)
- [Function Runtime](https://ipjohnson.github.io/Hardened.Docs/aws/lambda/function-runtime/)
- [Web Runtime (API Gateway)](https://ipjohnson.github.io/Hardened.Docs/aws/lambda/web-runtime/)
- [DDB Stream Processing](https://ipjohnson.github.io/Hardened.Docs/aws/lambda/ddb-stream/)
- [SQS Processing](https://ipjohnson.github.io/Hardened.Docs/aws/lambda/sqs-processing/)
- [DynamoDB Client](https://ipjohnson.github.io/Hardened.Docs/aws/clients/dynamodb/)
- [Lambda Testing](https://ipjohnson.github.io/Hardened.Docs/aws/lambda/testing/)

## Related Repositories

- [Hardened.Framework](https://github.com/ipjohnson/Hardened.Framework) — Core framework (DI, routing, testing)
- [Hardened.Canaries](https://github.com/ipjohnson/Hardened.Canaries) — Canary testing framework
- [Hardened.Docs](https://github.com/ipjohnson/Hardened.Docs) — Documentation site

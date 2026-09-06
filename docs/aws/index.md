# AWS

The handlers you wrote for Kestrel run on Lambda. The runtime module is the only thing that
changes:

```csharp
using Hardened.Amz.Web.Lambda.Runtime.DependencyInjection;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;

[HardenedModule]
[LambdaWebModule]
public partial class Application { }

public class ProductController {
    [Get("/api/products/{id}")]
    public Product GetProduct(string id) => _repository.Find(id);
}
```

A function that is not an HTTP API is a method with a name instead of a route:

```csharp
using Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;
using Hardened.Requests.Abstract.Attributes;

[HardenedModule]
[LambdaFunctionModule]
public partial class Application { }

public class OrderHandler {
    [HardenedFunction("process-order")]
    public OrderResponse ProcessOrder(OrderRequest request) => _orders.Process(request);
}
```

A handler, its filters, its parameter binding and its configuration do not know they are on
Lambda. `dotnet new hardened-web --host aws-lambda` and `dotnet new hardened-function` write
both shapes, with tests; see [Project templates](/guide/project-templates).

## What the runtime gives you

**Structured CloudWatch logging.** Each runtime replaces `ILoggerProvider` with one that writes
structured lines CloudWatch Logs Insights can query, with the request id attached.

**Embedded metrics.** `IMetricLogger` writes the CloudWatch Embedded Metric Format, so metrics
come out of the log stream without an extra API call.

**Partial batch responses.** The SQS and DynamoDB Stream runtimes fork the execution chain per
record and report exactly which records failed, so a batch of ten with one bad message redelivers
one message rather than ten.

**The log level from the environment.** `Information`, or `Debug` in `development` and `test`,
overridden by `LOG_LEVEL`. See [Log level](/guide/environments#log-level).

## Cold start

Routing tables, service registrations and parameter binding are all emitted during the build, so
a cold start does no assembly scanning and no reflection over your types. On its first invocation
the process constructs a service provider from a list of registrations and calls a method. The
runtimes are compatible with trimming and Native AOT: every registration is a literal `typeof()`
the trimmer can follow.

## Where things are

| Area | Page | Repository path |
|---|---|---|
| HTTP behind API Gateway | [API Gateway](/aws/lambda-web) | [`src/Lambda/Web`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/Web) |
| Plain Lambda functions | [Lambda functions](/aws/lambda-function) | [`src/Lambda/Function`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/Function) |
| SQS batches | [SQS](/aws/sqs) | [`src/Lambda/Sqs`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/Sqs) |
| DynamoDB stream records | [DynamoDB Streams](/aws/ddb-streams) | [`src/Lambda/DynamoDbStream`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/DynamoDbStream) |
| DynamoDB clients | [DynamoDB client](/aws/dynamodb) | [`src/Clients/DynamoDb`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Clients/DynamoDb) |
| Infrastructure | [CDK](/aws/cdk) | [`src/Hardened.Amz.Cdk`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Hardened.Amz.Cdk) |
| Test harnesses | [Testing AWS handlers](/aws/testing) | the `*.Testing` packages beside each runtime |

The AWS integrations live in [Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz) and ship
as their own packages, so an application that never touches AWS does not carry the SDK. The core
framework is [Hardened.Framework](https://github.com/ipjohnson/Hardened.Framework), covered in
the [Guide](/guide/getting-started).

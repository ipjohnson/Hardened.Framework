# AWS

The AWS integrations live in a separate repository and ship as their own packages, so an application
that never touches AWS never carries the SDK.

<div class="hd-repo">

**Source:** [github.com/ipjohnson/Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz)

Lambda runtimes for functions, API Gateway, DynamoDB Streams and SQS; DynamoDB and SQS client
libraries; CDK constructs; and the test harnesses for all of them.

</div>

## What is here

| Area | Page | Repository path |
|---|---|---|
| Plain Lambda functions | [Lambda functions](/aws/lambda-function) | [`src/Lambda/Function`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/Function) |
| HTTP behind API Gateway | [API Gateway](/aws/lambda-web) | [`src/Lambda/Web`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/Web) |
| DynamoDB stream records | [DynamoDB Streams](/aws/ddb-streams) | [`src/Lambda/DynamoDbStream`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/DynamoDbStream) |
| SQS batches | [SQS](/aws/sqs) | [`src/Lambda/Sqs`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/Sqs) |
| DynamoDB clients | [DynamoDB client](/aws/dynamodb) | [`src/Clients/DynamoDb`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Clients/DynamoDb) |
| Infrastructure | [CDK](/aws/cdk) | [`src/Hardened.Amz.Cdk`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Hardened.Amz.Cdk) |
| Test harnesses | [Testing AWS handlers](/aws/testing) | — |

## The shape of it

A Lambda application is a [module](/guide/modules) like any other. What changes is which runtime
module it imports — and that is all that changes, because everything above the runtime is the core
framework. A handler, its filters, its parameter binding and its configuration do not know they are
on Lambda.

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Shared.Runtime.Attributes;

[HardenedModule]
public partial class Application { }

public class OrderHandler {
    [HardenedFunction("process-order")]
    public OrderResponse ProcessOrder(OrderRequest request) {
        return new OrderResponse { OrderId = Guid.NewGuid().ToString() };
    }
}
```

The same handler behind API Gateway is a route rather than a function name:

```csharp
[HardenedModule]
[LambdaWebApplication(Version = ProxyIntegrationType.HttpApiV2)]
public partial class Application { }

public class ProductController {
    [Get("/api/products/{id}")]
    public Product GetProduct(string id) => _repository.Find(id);
}
```

## What the runtime gives you

**Structured CloudWatch logging.** Each runtime replaces `ILoggerProvider` with one that writes
structured lines CloudWatch Logs Insights can query, with the request id attached.

**Embedded metrics.** `IMetricLogger` writes the CloudWatch Embedded Metric Format, so metrics come
out of the log stream without an extra API call — and therefore without adding latency to a request
or failing it when CloudWatch is throttled.

**Partial batch responses.** The SQS and DynamoDB Stream runtimes fork the execution chain per
record and report exactly which records failed, so a batch of ten with one bad message redelivers
one message rather than ten.

**The log level from the environment.** `Information`, or `Debug` in `development` and `test`,
overridden by `LOG_LEVEL`. See [Environments](/guide/environments#log-level).

## Cold start

The reason the framework is built this way shows up most clearly here. Routing tables, service
registrations and parameter binding are all emitted during the build, so a cold start does no
assembly scanning and no reflection over your types. What the process does on its first invocation
is construct a service provider from a list of registrations and call a method.

That also makes the runtimes compatible with trimming and Native AOT, because there is nothing for
the trimmer to lose track of — every registration is a literal `typeof()` it can follow.

## The core repository

- [Hardened.Framework](https://github.com/ipjohnson/Hardened.Framework) — modules, dependency
  injection, routing, binding and testing, covered in the [Guide](/guide/getting-started)

<style>
.hd-repo {
  border: 1px solid var(--vp-c-divider);
  border-left: 4px solid var(--vp-c-brand-1);
  border-radius: 8px;
  padding: 16px 20px;
  margin: 24px 0;
  background: var(--vp-c-bg-soft);
}

.hd-repo p:last-child {
  margin-bottom: 0;
}
</style>

# Hardened.Amz

AWS integrations for the [Hardened](https://ipjohnson.github.io/Hardened.Docs) ecosystem — Lambda runtimes, DynamoDB and SQS client libraries, and CDK support.

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

### Client Libraries

| Package | Description |
|---|---|
| `Hardened.Amz.DynamoDbClient` | `IDynamoDbClientProvider`, DynamoDB extensions |
| `Hardened.Amz.DynamoDbClient.Testing` | `[LocalDynamoDb]` with Testcontainers |
| `Hardened.Amz.SqsClient` | `ISqsClient` for SQS messaging |

### Infrastructure

| Package | Description |
|---|---|
| `Hardened.Amz.Cdk` | AWS CDK constructs |

## Quick Start

### Lambda Function

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Requests.Abstract.Attributes;

[HardenedModule]
public partial class Application { }

public class OrderHandler {
    [HardenedFunction("process-order")]
    public OrderResponse ProcessOrder(OrderRequest request) {
        return new OrderResponse { OrderId = Guid.NewGuid().ToString() };
    }
}
```

### Lambda Web API (API Gateway)

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Amz.Web.Lambda.Runtime;

[HardenedModule]
[LambdaWebApplication(Version = ProxyIntegrationType.HttpApiV2)]
public partial class Application { }

public class ProductController {
    [Get("/api/products/{id}")]
    public Product GetProduct(string id) {
        return new Product { Id = id, Name = "Widget" };
    }
}
```

## Documentation

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

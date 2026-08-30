# Lambda functions

A Lambda function is a method marked `[HardenedFunction]`. The generator emits the entry point, the
payload deserialisation, the parameter binding and the response serialisation.

**Source:** [`src/Lambda/Function`](https://github.com/ipjohnson/Hardened.Amz/tree/main/src/Lambda/Function)
in [Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz).

## Packages

```xml
<ItemGroup>
    <PackageReference Include="Hardened.Amz.Function.Lambda.Runtime" Version="..." />
    <PackageReference Include="Hardened.Amz.Function.Lambda.SourceGenerator" Version="..."
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

Source generators are referenced as analysers. `ReferenceOutputAssembly="false"` keeps the generator
itself out of the published output.

## A function

```csharp
using Hardened.Amz.Function.Lambda.Runtime.DependencyInjection;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Shared.Runtime.Attributes;

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

`[LambdaFunctionModule]` brings the invocation path and, through the `[HardenedRequestModule]` it
carries, the request pipeline. It is not optional: an application without it compiles and then fails
at construction, naming the missing attribute.

The string names the function. Several functions can live in one assembly and one deployment
artefact, each selected by name, so a service ships as a set of Lambdas without a project per
Lambda. Omit the name and the method name is used.

The payload is deserialised into `request`. Parameters bind as they do
[everywhere else](/guide/parameter-binding): a registered service type comes from the container, and
what is left is the payload.

## The Lambda context

The runtime puts the invocation's `ILambdaContext` on a registered accessor, so anything in the call
stack can reach it without threading it through every signature:

```csharp
public interface ILambdaContextAccessor {
    ILambdaContext? Context { get; set; }
}
```

```csharp
public class OrderHandler {
    private readonly ILambdaContextAccessor _context;

    public OrderHandler(ILambdaContextAccessor context) {
        _context = context;
    }

    [HardenedFunction("process-order")]
    public async Task<OrderResponse> ProcessOrder(OrderRequest request, IOrderService orders) {
        if (_context.Context!.RemainingTime < TimeSpan.FromSeconds(5)) {
            return OrderResponse.Deferred();
        }

        return await orders.Process(request);
    }
}
```

`[FromContext("name")]` is the other route: it binds a named value out of the invocation's header
collection, the same source `[FromHeader]` reads on the web side.

## Errors

By default an exception is caught, serialised and returned as the function's response, and the
invocation is recorded as a success. That suits a synchronous caller that wants a structured error.

When the invocation should *fail*, so that the caller's retry policy, a dead letter queue or an
alarm sees it, apply `[ThrowException]`:

```csharp
[HardenedFunction("process-order")]
[ThrowException]
public OrderResponse ProcessOrder(OrderRequest request) => _orders.Process(request);
```

The filter runs after the handler and rethrows whatever landed in `Response.ExceptionValue`, so the
Lambda invocation errors.

::: warning This choice is invisible until something breaks
An asynchronous Lambda that swallows its exceptions retries nothing and alarms on nothing — the
invocation succeeded, it just returned an error object nobody reads.
:::

## Logging and metrics

The runtime replaces the logger provider with one that writes structured lines, so
`ILogger<T>` output is queryable in CloudWatch Logs Insights:

```csharp
public class OrderHandler {
    private readonly ILogger<OrderHandler> _logger;

    public OrderHandler(ILogger<OrderHandler> logger) {
        _logger = logger;
    }

    [HardenedFunction("process-order")]
    public OrderResponse ProcessOrder(OrderRequest request) {
        _logger.LogInformation("Processing {OrderId}", request.OrderId);

        return _orders.Process(request);
    }
}
```

Named placeholders become fields in the log line rather than being flattened into the message.

`IMetricLogger` records to the CloudWatch Embedded Metric Format, emitted through the log stream:

```csharp
context.RequestMetrics.Record(OrderMetrics.ProcessingDuration, elapsed);
context.RequestMetrics.Tag("region", "us-west-2");
```

No API call, and no added latency.

## Testing

```csharp
[assembly: LambdaFunctionTesting]
[assembly: HardenedTestEntryPoint(typeof(Application))]
```

```csharp
[HardenedTest]
public async Task ProcessesAnOrder(LambdaTestApp app) {
    var response = await app.Invoke<OrderResponse>(
        "process-order", new OrderRequest { Sku = "SKU-1" });

    Assert.NotNull(response.OrderId);
}
```

See [Testing AWS handlers](/aws/testing) for the raw-JSON variant, the context callback, and the
batch harnesses.

# The execution pipeline

Every request — an HTTP call, a Lambda invocation, an SQS message, a stream record — runs through
the same pipeline. A pipeline is an ordered list of filters, and the handler you wrote is the last
one.

## The chain

```csharp
public interface IExecutionFilter {
    Task Execute(IExecutionChain chain);
}
```

```csharp
public interface IExecutionChain {
    Task Next();
    IExecutionContext Context { get; }
    IExecutionChain Fork(IExecutionContext context);
    bool IsLastFilter { get; }
}
```

A filter does its work around `chain.Next()`:

```csharp
public class TimingFilter : IExecutionFilter {
    public async Task Execute(IExecutionChain chain) {
        var start = MachineTimestamp.Now;

        try {
            await chain.Next();
        }
        finally {
            chain.Context.RequestMetrics.Record(
                RequestMetrics.TotalRequestDuration, start.GetElapsedMilliseconds());
        }
    }
}
```

Not calling `Next()` short-circuits everything after it, which is how authorisation and caching
filters return without reaching the handler.

## The context

`IExecutionContext` is what the filters share:

| Member | What it holds |
|---|---|
| `Request` | Method, path, headers, query string, path tokens, body stream |
| `Response` | Status, headers, cookies, content type, `ResponseValue`, `ExceptionValue` |
| `RequestServices` | The request's service scope |
| `RootServiceProvider` | The application's provider |
| `HandlerInstance` / `HandlerInfo` | The handler being invoked, and its metadata |
| `RequestMetrics` | An `IMetricLogger` scoped to this request |
| `StartTime` | A `MachineTimestamp` taken when the request began |
| `CancellationToken` | Where the platform supplies one |

The response carries a *value*, not bytes. `Response.ResponseValue` is what the handler returned;
serialisation is a filter later in the chain, so a filter that wants to change the payload changes
the value.

Which serializer writes it is decided per request from the client's `Accept` header — see
[Content negotiation](/guide/content-negotiation).

## Ordering

Filters are sorted by an integer. Lower runs earlier — that is, further from the handler.

`ExecutionFilterOrder` names the framework's positions:

| Name | Value |
|---|---|
| `Init` | `-10000` |
| `FullRequestMetrics` | `-7000` |
| `RetryFilter` | `-5000` |
| `BeforeSerialize` | `-1` |
| `BindParameters` | `0` |
| `First`, `Second`, `Third` | `1`, `2`, `3` |
| `Normal` | `100` |
| `Last` | `int.MaxValue` |

`FilterOrder` names the positions used when registering through the filter registry:

| Name | Value |
|---|---|
| `HandlerCreation` | `-1000` |
| `BeforeSerialization` | `4` |
| `Serialization` | `5` |
| `Validation` | `6` |
| `DefaultValue` | `1000` |
| `EndPointHandlers`, `EndPointInvoke` | `2000` |

A filter that must see the response value picks a number below `Serialization`; one that must see
the bytes picks a number above it.

## Attaching a filter to one handler

An attribute implementing `IRequestFilterProvider` contributes filters to the handler it is applied
to. `[Retry]` is the shipped example:

```csharp
public class RetryAttribute : Attribute, IRequestFilterProvider {
    public int Retries { get; set; } = 3;

    public int SleepTime { get; set; } = 500;

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        yield return new RequestFilterInfo(
            context => new RetryFilter(
                context.RequestServices.GetRequiredService<IMemoryStreamPool>(),
                Retries,
                SleepTime),
            FilterOrder.HandlerCreation - 10);
    }
}
```

Applied like any other attribute:

```csharp
[Post("/int/add")]
[Retry(Retries = 4)]
public int Add(IMathService<int> mathService, MathAddModel model) =>
    mathService.Add(model.Values.ToArray());
```

`RequestFilterInfo` takes a *factory*, not an instance, so a filter can depend on request-scoped
services. `GetFilters` receives the handler's metadata, so one attribute can behave differently
depending on what it was applied to.

::: tip Retry needs a rewindable body
`RetryFilter` takes the memory stream pool because retrying a request means replaying its body,
which is also why it sits at `HandlerCreation - 10` — early enough to capture the body before
anything consumes it.
:::

## Attaching a filter to everything

`IGlobalFilterRegistry` registers across all handlers. Do it from an `IStartupService`:

```csharp
public class RegisterFilters : IStartupService {
    public Task<bool> Startup(IServiceProvider rootProvider) {
        var registry = rootProvider.GetRequiredService<IGlobalFilterRegistry>();

        registry.RegisterFilter(new CorrelationIdFilter(), FilterOrder.HandlerCreation);

        return Task.FromResult(true);
    }
}
```

The second overload decides per handler, returning `null` to skip:

```csharp
registry.RegisterFilter(handlerInfo =>
    handlerInfo.Path.StartsWith("/admin")
        ? new RequestFilterInfo(_ => new AdminAuditFilter(), FilterOrder.HandlerCreation)
        : null);
```

## Forking

`chain.Fork(context)` copies the remainder of the chain so it can be run again — with a cloned
context, a cloned request, or a fresh response. Retry uses it to re-run the handler after a failure
without re-running the filters that already succeeded, and the batch runtimes use it to run one
chain per record.

`IExecutionContext.Clone`, `IExecutionRequest.Clone` and `IExecutionResponse.Clone` each take
optional replacements, so a fork can change one thing and keep the rest.

## Middleware

Above the filters sits `IMiddlewareService`, which is where a host inserts the pipeline. This is
what `app.UseHardened()` does under ASP.NET Core, and what the Lambda runtimes do when they start.
It is also what
[a test drives the real pipeline](/guide/testing-web) through.

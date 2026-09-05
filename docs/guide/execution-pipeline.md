# The execution pipeline

Every request runs through the same pipeline: an HTTP call, a Lambda invocation, an SQS message,
a stream record. The pipeline is an ordered list of filters, and the handler you wrote is the last
one. An attribute adds a filter to one handler:

```csharp
[Get("/rates/{symbol}")]
[Retry(Attempts = 4)]
public Rate Read(string symbol) => _upstream.Latest(symbol);
```

A filter of your own is a class that does its work around `chain.Next()`:

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

Not calling `Next()` short-circuits everything after it, which is how the authorization and
caching filters answer without reaching the handler.

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
| `CancellationToken` | Where the platform supplies one. Replaced for the span of a [request timeout](/guide/request-timeouts), and put back |

The response carries a value, not bytes. `Response.ResponseValue` is what the handler returned.
Serialization is a filter later in the chain, so a filter that wants to change the payload changes
the value. Which serializer writes it is decided per request from the `Accept` header; see
[Content negotiation](/guide/content-negotiation).

## Ordering

Filters are sorted by an integer. Lower runs earlier, which is to say further from the handler.
`FilterOrder` names the positions:

| Stage | Value | What sits there |
|---|---|---|
| `HandlerCreation` | `-10000` | Creating the handler instance, and `[Retry]`'s body capture |
| `RateLimitTransport` | `1000` | [Refusing on volume](/guide/rate-limiting), before anyone has asked who is calling |
| `Authentication` | `2000` | [Establishing who the caller is](/guide/authentication) |
| `RateLimitPrincipal` | `3000` | Refusing on volume, once it is known whose volume it is |
| `GrantAuthorization` | `4000` | [Deciding from grants](/guide/authorization) alone |
| `Conditional` | `5000` | Answering a [conditional GET](/guide/conditional-requests) with a 304 |
| `ResponseCache` | `6000` | Serving a [stored response](/guide/response-caching) instead of running the handler |
| `BeforeSerialization` | `6500` | `Before + Serialization`, where `[CacheControl]` and the [request timeout](/guide/request-timeouts) sit |
| `Serialization` | `7000` | Binding the request, and serializing the response |
| `Validation` | `8000` | Checking the constraints, over the parameters just bound |
| `Authorization` | `9000` | Deciding from the resource as well as the caller |
| `Retry` | `10000` | Re-running the handler after a failure |
| `DefaultValue` | `100000` | Where a filter that states no order lands |
| `EndPointHandlers`, `EndPointInvoke` | `200000` | The handler. Terminal, so a filter ordered above it is never reached |

Each stage is a thousand from its neighbour. `Before` and `After` are half a gap, for sitting
between two stages:

```csharp
FilterOrder.Before + FilterOrder.ResponseCache   // just outside the cache
FilterOrder.After + FilterOrder.Authentication   // just inside authentication
```

They do not compose. `Before + Before + Serialization` is a whole gap and lands exactly on
`ResponseCache`. Name the earlier stage instead.

### The line at serialization

Everything ahead of `Serialization` can refuse a request before its body has been read, and those
stages run in cheapest-refusal-first order. A filter on that side of the line refuses by recording
the failure on the response and calling `Next()` anyway, so the serialization filter can write it.
Behind the line, an ordinary short circuit is what stops the handler.

Throwing from ahead of the line unwinds past the only thing that would have written a body, and
the caller gets a bare 500.

### Seeing what a chain composed into

A handler's chain is assembled once, from the attributes on the method and its class, every global
provider and the pinned filters, then sorted and reduced to factories. Enable the
`Hardened.Requests.Pipeline` category at Debug and each one is written as it is built:

```
GET /orders filter chain: InstanceStandIn@-10000, TenantProvider@2000, IoStandIn@7000,
Generic@8000, TenantProvider@10000, InvokeNoParametersFilter@200000
```

Each filter and its order, in the order they run. That answers "did my filter land where I meant?",
and it shows an attribute that never reached the metadata or a global filter that stood down. A
chain is composed once per handler, so an application that has not enabled the category pays one
`IsEnabled` check per handler and nothing per request.

A filter is named by the registration, or failing that by the type that made it. Give
`RequestFilterInfo` a name to control what appears:

```csharp
yield return new RequestFilterInfo(
    _ => new AdminAuditFilter(), FilterOrder.HandlerCreation, nameof(AdminAuditFilter));
```

## Attaching a filter to one handler

An attribute implementing `IRequestFilterProvider` contributes filters to the handler it is applied
to. `[Retry]` is the shipped example:

```csharp
public class RetryAttribute : Attribute, IRequestFilterProvider {
    public int Attempts { get; set; } = 3;

    public int SleepTime { get; set; } = 500;

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        yield return new RequestFilterInfo(
            _ => new RetryFilter(Attempts, SleepTime, TotalBudget, AllowNonIdempotent),
            FilterOrder.Retry,
            nameof(RetryFilter));
    }
}
```

`RequestFilterInfo` takes a factory, not an instance, so a filter can depend on request-scoped
services. `GetFilters` receives the handler's metadata, so one attribute can behave differently
depending on what it was applied to.

`[Retry]` declines client errors, and refuses a non-idempotent verb unless
`AllowNonIdempotent = true`. `TotalBudget` bounds the whole thing at 10 seconds by default,
because the caller is waiting for every attempt.

::: tip Retry runs behind serialization, and shares the handler instance
The filter at `Serialization` catches whatever the handler failed with and records it on the
response. A retry filter behind it sees that failure and runs the handler again, and the response
is serialized once when the retry is done rather than once per attempt. It also sits behind
`Authorization`, because a refusal is not transient.

The handler instance is created once, at `HandlerCreation`, and every attempt shares it. A handler
that keeps mutable per-request state on itself cannot be retried.
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

`chain.Fork(context)` copies the remainder of the chain so it can be run again, with a cloned
context, a cloned request, or a fresh response. Retry uses it to re-run the handler after a failure
without re-running the filters that already succeeded, and the batch runtimes use it to run one
chain per record.

`IExecutionContext.Clone`, `IExecutionRequest.Clone` and `IExecutionResponse.Clone` each take
optional replacements, so a fork can change one thing and keep the rest.

## Middleware

Above the filters sits `IMiddlewareService`, which is where a host inserts the pipeline. It is
what `app.UseHardened()` does under ASP.NET Core, what the Lambda runtimes do when they start, and
what [a test](/guide/testing-web) drives the real pipeline through.

## Next

- [Request timeouts](/guide/request-timeouts): a shipped filter and where it sits
- [Rate limiting](/guide/rate-limiting): a filter that refuses ahead of the line
- [Sending requests](/guide/testing-web): every filter runs in a test

# Request timeouts

`[Timeout]` bounds how long an operation may take. The budget reaches the handler as the
`CancellationToken` it binds, and a handler that observes it answers 504 when the budget runs
out.

```csharp
[Get("/rates/{symbol}")]
[Timeout(Milliseconds = 2000)]
public Task<Rate> Read(string symbol, CancellationToken cancellationToken) =>
    _upstream.Latest(symbol, cancellationToken);
```

```
GET /rates/EUR
HTTP/1.1 504 Gateway Timeout
```

| | Default |
|---|---|
| `Milliseconds` | 30000 |
| `Status` | 504 |
| `RetryAfterSeconds` | none |

There is no default policy. An operation nothing declares a budget for is not bounded, gets no
timer, and its cancellation is a 500 rather than a 504, because there was no deadline to have
missed.

## Bound more than one operation at a time

Four places can declare a budget, and the nearest one to the handler wins:

```csharp
[Timeout(Milliseconds = 2000)]              // this operation, or every method on this class
public class RateController { }

[assembly: Timeout(Milliseconds = 2000)]    // every handler in this project
```

```csharp
[HardenedModule]
[Enable<RequestTimeouts>]                   // the default budget, 30 seconds
[KestrelRuntime]
public partial class Application { }

[HardenedModule]
[RequestTimeouts(5000)]                     // the same, with the number written
[KestrelRuntime]
public partial class Application { }
```

Operation, then its class, then the handler's own assembly, then the entry point. Nothing is
combined: two budgets do not compose into a third, so the nearest declaration is the answer and
the rest are fallbacks. Nearest wins in both directions, so a method may run longer than its
neighbours:

```csharp
[Timeout(Milliseconds = 20_000)]
public class ReportController {

    [Get("/reports/summary")]
    public Task<Summary> Summary() => _reports.Summary();          // 20 seconds

    [Get("/reports/annual")]
    [Timeout(Milliseconds = 120_000)]
    public Task<Annual> Annual() => _reports.Annual();             // two minutes
}
```

The rung is the handler's assembly, so `[assembly: Timeout]` beside an entry point covers that
project's own handlers and not a referenced library's, and a library's `[assembly: Timeout]`
beats the host's default.

`[Enable<RequestTimeouts>]` takes no arguments, because `[Enable<T>]` is one attribute name shared
by every optional feature. Write `[RequestTimeouts(5000)]` when the number matters. Say it once.

## Bound a class of handlers by rule

A convention states what a whole class of operations may take:

```csharp
public class SearchIsAlwaysFast : IRequestTimeoutConvention {
    public TimeoutPolicy? Apply(IExecutionRequestHandlerInfo handler) =>
        handler.Path.StartsWith("/search") ? new TimeoutPolicy(2000) : null;
}
```

```csharp
services.AddSingleton<IRequestTimeoutConvention, SearchIsAlwaysFast>();
```

It is asked once per handler as its filter chain is built. A convention can only tighten: it can
bound a handler that declared nothing and shorten one that declared too much, and it cannot hand
an operation that wrote `[Timeout(Milliseconds = 2000)]` a minute.

## Choose what the caller is told

504 by default. An operation shedding load rather than waiting on a dependency says so:

```csharp
[Timeout(Milliseconds = 2000, Status = 503, RetryAfterSeconds = 30)]
```

`RetryAfterSeconds` is only honest alongside 503. A deadline out at a dependency knows nothing
about when that dependency recovers, so the default sends no header. Only a declaration on the
operation, its class or its assembly can set the status; the entry point's default always
answers 504.

A client that hangs up on a bounded operation is reported the same way, because both cancel the
same token. The `RequestTimedOut` metric counts only the deadline, so a slow handler is not buried
under people closing tabs.

## Declare it in a description

Both specification-first front ends carry a deadline:

```yaml
paths:
  /rates:
    get:
      operationId: readRates
      x-hardened-timeout: 2000          # or { milliseconds, status, retryAfterSeconds }
```

```smithy
use hardened.api#timeout

@http(method: "GET", uri: "/rates")
@timeout(milliseconds: 2000)
operation ReadRates { }
```

The Smithy trait is defined in `hardened.smithy`, which the build adds to your model. A described
budget is a rung of the same cascade, so a `[Timeout]` on the generated implementation overrides
it exactly as a method overrides its class.

It round-trips. A code-first application's [exported document](/guide/openapi-document) carries
`x-hardened-timeout` for every operation the cascade bounds, so a service regenerated from that
contract is bounded the way the one that published it was. A bounded operation also publishes
the status it can be answered with, so a [generated client](/guide/clients) has a typed case for
the refusal.

## Where it sits in the pipeline

One filter per handler, one half-gap ahead of `FilterOrder.Serialization`:

| Inside the budget | Outside it |
|---|---|
| Parameter binding, validation, resource authorization | The conditional-GET flush |
| Every `[Retry]` attempt, under one budget rather than one each | The response cache's store, and its read |
| The handler | Compression's outward encode |

The position is not adjustable. A handler's declared `CancellationToken` parameter is copied out
of the context as the request is bound, so a filter placed any later would hand the handler the
transport's token. Placed any earlier, a request that spent its whole budget would have its
answer flushed and its cache entry written on an already-cancelled token.

## Not built

- **Cancellation is cooperative.** The budget cancels a token; it does not take a thread back. A
  handler that blocks, or that awaits something without passing the token, runs to completion and
  answers late. Pass the token to everything you await.
- **A started response cannot be recalled.** A deadline firing mid-stream cuts the body, with no
  status left to send. A [streaming handler](/guide/streaming) should bound its own work.
- **AWS Lambda.** The Lambda execution contexts do not yet support replacing the request's token,
  so a `[Timeout]` there fails the request rather than bounding it. A function that declares no
  budget is unaffected. Client disconnect is not observable on Lambda either.
- **The entry point's budget is not published.** A host-wide default is a deployment property
  rather than part of an operation's contract.

## Next

- [Rate limiting](/guide/rate-limiting): the other bound on an operation
- [The execution pipeline](/guide/execution-pipeline#ordering): the stages inside and outside the budget
- [Generating from OpenAPI](/guide/openapi#a-deadline-from-the-description): `x-hardened-timeout` in a contract

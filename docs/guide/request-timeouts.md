# Request timeouts

`[Timeout]` bounds how long an operation may take.

```csharp
[Get("/rates/{symbol}")]
[Timeout(Milliseconds = 2000)]
public Task<Rate> Read(string symbol, CancellationToken cancellationToken) =>
    _upstream.Latest(symbol, cancellationToken);
```

The budget reaches the handler as the `CancellationToken` it binds. When it runs out the token is
cancelled, and a handler that observes it answers 504.

| | Default |
|---|---|
| `Milliseconds` | 30000 |
| `Status` | 504 |
| `RetryAfterSeconds` | none |

**There is no default policy.** An operation nothing declares a budget for is not bounded, gets no
timer, and its cancellation is a 500 rather than a 504, because there was no deadline to have
missed. ASP.NET Core draws the line in the same place: its `DefaultPolicy` is null until an
application sets one.

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
combined: unlike an authorization requirement, two budgets do not compose into a third, so the
nearest declaration is the answer and the rest are fallbacks.

Nearest wins in both directions, so a method may deliberately run *longer* than its neighbours:

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

**The assembly beats the entry point**, which matters when a library ships handlers. A
`[WebLibrary]` project writing `[assembly: Timeout]` is saying something specific about its own
handlers; an entry point is stating a fallback for handlers that said nothing. Read the other way
round, a host would silently loosen a bound the library set deliberately. The rung is the
*handler's* assembly, so `[assembly: Timeout]` written beside an entry point covers that project's
own handlers and not a referenced library's.

`[Enable<RequestTimeouts>]` takes no arguments, because `[Enable<T>]` is one attribute name shared
by every optional feature. Write `[RequestTimeouts(5000)]` when the number matters. Both register a
default and the tighter one applies, but say it once.

## Bound a class of handlers by rule

A convention states what a whole class of operations may take, without the rule being copied onto
every method it covers:

```csharp
public class SearchIsAlwaysFast : IRequestTimeoutConvention {
    public TimeoutPolicy? Apply(IExecutionRequestHandlerInfo handler) =>
        handler.Path.StartsWith("/search") ? new TimeoutPolicy(2000) : null;
}
```

```csharp
services.AddSingleton<IRequestTimeoutConvention, SearchIsAlwaysFast>();
```

Asked once per handler as its filter chain is built, and returning null for most of them costs
nothing.

**A convention can only tighten.** It can bound a handler that declared nothing and shorten one
that declared too much, and it cannot hand an operation that wrote `[Timeout(Milliseconds = 2000)]`
a minute. Loosening is the one direction where a rule written far from the handler is likelier to
be wrong than the handler is, and `IAuthorizationConvention` follows the same rule.

## Choose what the caller is told

504 by default. Not 408, which is a request that never finished arriving rather than one that took
too long, and 504 is what ASP.NET Core's request-timeout middleware answers too.

An operation shedding load rather than waiting on a dependency says so:

```csharp
[Timeout(Milliseconds = 2000, Status = 503, RetryAfterSeconds = 30)]
```

`RetryAfterSeconds` is only honest alongside 503. A deadline out at a dependency knows nothing about
when that dependency recovers, so the default sends no header.

Only a declaration on the operation, its class or its assembly can say this. The entry point's
default always answers 504.

A client that hangs up on a bounded operation is reported the same way, because both cancel the same
token. Nobody receives either response. The `RequestTimedOut` metric counts only the deadline, so a
slow handler is not buried under people closing tabs.

## Declare it in a description

Both specification-first front ends carry a deadline, so a service generated from a description is
bounded the way its author wrote it:

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

Neither language has a field for this, and that is not an oversight in either: a specification
describes the exchange, and how long a server may take over it is a property of the server. The
Smithy trait is defined in `hardened.smithy`, which the build adds to your model, so `@timeout`
needs no wiring.

A described budget is a rung of the same cascade, so a `[Timeout]` on the generated implementation
overrides it exactly as a method overrides its class.

It round-trips. A code-first application's [exported document](/guide/openapi-document) carries
`x-hardened-timeout` for every operation the cascade bounds, so a service regenerated from that
contract is bounded the way the one that published it was.

## What the document says

A bounded operation publishes the status it can be answered with, so a
[generated client](/guide/clients) has a case for the refusal it will actually be sent rather than a
bare transport exception. `[AuthorizeGrants]` and `[RateLimit]` publish their 403 and 429 the same
way.

## Where it sits in the pipeline

One filter per handler, one half-gap ahead of `FilterOrder.Serialization`.

| Inside the budget | Outside it |
|---|---|
| Parameter binding, validation, resource authorization | The conditional-GET flush |
| Every `[Retry]` attempt, under one budget rather than one each | The response cache's store, and its read |
| The handler | Compression's outward encode |

The position is not adjustable, and it is what makes the feature work at all: a handler's declared
`CancellationToken` parameter is copied out of the context as the request is bound, so a filter
placed any later would hand the handler the transport's token and the budget would reach nothing.
Placed any earlier, a request that spent its whole budget would have its answer flushed and its
cache entry written on an already-cancelled token.

One consequence worth knowing: a `[Retry]` under a `[Timeout]` gets one budget across every attempt
rather than one per attempt.

## Not built

- **Cancellation is cooperative.** The budget cancels a token; it does not take a thread back. A
  handler that blocks, or that awaits something without passing the token, runs to completion and
  answers late. Pass the token to everything you await.
- **A started response cannot be recalled.** A deadline firing mid-stream cuts the body, with no
  status left to send. A [streaming handler](/guide/streaming) should bound its own work.
- **AWS Lambda.** The Lambda execution contexts do not yet support replacing the request's token, so
  a `[Timeout]` there fails the request rather than bounding it. A function that declares no budget
  is unaffected. Client disconnect is not observable on Lambda either.
- **The entry point's budget is not published.** A host-wide default is a deployment property rather
  than part of an operation's contract, so it does not appear on the document.

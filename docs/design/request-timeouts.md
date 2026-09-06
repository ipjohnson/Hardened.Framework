# Request timeouts

A deadline is a property of an operation, not a filter bolted to one.
`IExecutionRequestHandlerInfo.Timeout` is what a handler may take and what its caller is told when
it does not finish, and everything that needs to know reads it there: the filter that enforces it,
the converter that turns its expiry into a status, a log line, an application's own code.

**There is no default policy.** Nothing is bounded until something declares one, which is where
ASP.NET Core draws the line too: its `RequestTimeoutOptions.DefaultPolicy` is null until an
application sets it. A handler nothing declares a budget for gets no filter, no linked
`CancellationTokenSource` and no timer, and the token it binds is the host's, which cancels when the
client hangs up and not on any duration. Bounding execution is the whole of what declaring a policy
buys: without one, a handler wedged on a dependency holds its request slot indefinitely as far as
this framework is concerned.

## Declaring it in a description

Both front ends carry a deadline, and a service generated from a description is bounded the way its
author wrote it rather than only where somebody remembered an attribute.

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
describes the exchange, and how long a server may take over it is a property of the server. So both
spellings are Hardened's own vocabulary. The Smithy trait is defined in `hardened.smithy`, which the
targets add to a project's model, so a model can write `@timeout` without wiring a file it did not
author.

A declared budget reaches the generated handler as the same `[Timeout]` a code-first handler carries,
in its metadata. It is therefore a rung of the cascade rather than a separate mechanism: a
`[Timeout]` written on the generated implementation's method or class overrides what the description
said, by the same nearest-wins rule as everything else.

It round-trips. A code-first application's exported document carries `x-hardened-timeout` for every
operation the cascade bounds, in the scalar form where the status and retry-after are the defaults
and as an object where they are not, so a service or client regenerated from that contract is
bounded the way the one that published it was.

The entry point's default is the one rung the document does not carry, deliberately: a host-wide
knob is a deployment property rather than part of the contract an operation publishes.

## Where a deadline comes from

Four places, nearest to the handler first. Nothing is combined: unlike a requirement, two budgets do
not compose into a third, so the nearest declaration is the answer and the rest are fallbacks.

| Rung | Written as |
|---|---|
| The operation | `[Timeout(Milliseconds = 2000)]` on the method |
| Its class | `[Timeout(Milliseconds = 2000)]` on the controller |
| The handler's assembly | `[assembly: Timeout(Milliseconds = 2000)]` |
| The entry point | `[Enable<RequestTimeouts>]` or `[RequestTimeouts(5000)]` |

Nearest wins in both directions. A method loosens or tightens what its class declared, and a class
does the same to its assembly. That is why resolution takes the *first* declaration a handler
carries rather than the smallest: a tightest-wins rule could not express a method that deliberately
runs longer than its neighbours.

**The assembly beats the entry point.** A `[WebLibrary]` project writing `[assembly: Timeout]` is
saying something specific about its own handlers; an entry point is stating a blanket fallback for
handlers that said nothing. Read the other way round, a host would silently loosen a bound a library
deliberately set. The consequence worth knowing is that the rung is the *handler's* assembly, so
`[assembly: Timeout]` written beside an entry point covers that assembly's own handlers and not a
referenced library's.

A budget has to be greater than zero. Anything else fails as the handler's chain is composed,
naming the handler and the rung it came from, which is once at startup rather than once a request.

## The entry point's two spellings

```csharp
[HardenedModule]
[Enable<RequestTimeouts>]      // the default budget, 30 seconds
[KestrelRuntime]
public partial class Application { }
```

```csharp
[HardenedModule]
[RequestTimeouts(5000)]        // the budget written
[KestrelRuntime]
public partial class Application { }
```

`[Enable<T>]` is one attribute name shared by every optional feature and takes no arguments. The
generator turns it into `AddModule(new RequestTimeouts())`, so a number cannot ride on it. The
attribute DependencyModules generates from the module's own constructor is where a number goes.
Writing both registers two defaults and the tighter applies, which is defined rather than
surprising, but say it once.

A number on the module has to be a constructor parameter rather than a property. DependencyModules
gives every settable property a slot on the generated attribute defaulting to `default(T)` and
copies it onto the module under a null guard, and a value type always passes that guard, so a
settable `int Milliseconds` would be overwritten with 0 by the very attribute that supplied it.
`HardenedOpenApiUi` and `HardenedStaticContent` make every property of theirs nullable for the same
reason.

An application wanting a non-504 default writes `[assembly: Timeout(Milliseconds = 5000, Status =
503)]` instead. That carries the status too, and being the handler's own assembly it is the more
specific statement anyway.

## Conventions

A rule that covers a class of handlers, rather than one copied onto every method it applies to:

```csharp
public class SearchIsAlwaysFast : IRequestTimeoutConvention {
    public TimeoutPolicy? Apply(IExecutionRequestHandlerInfo handler) =>
        handler.Path.StartsWith("/search") ? new TimeoutPolicy(2000) : null;
}
```

Registered as a singleton, asked once per handler as its chain is built, null for "nothing to say".

**A convention can only tighten.** What it returns is folded in with `TimeoutPolicy.Tighter`, never
substituted, so it can bound a handler that declared nothing and shorten one that declared too much,
and cannot hand an operation that wrote `[Timeout(Milliseconds = 2000)]` a minute. Loosening is the
one direction where a rule written far from the handler is likelier to be wrong than the handler is.
`IAuthorizationConvention` follows the same rule for the same reason.

## What a caller is told

A budget that runs out is **504**. Not 408, which is a request that never finished arriving and
which `RequestTimeout`'s own remarks refuse for this. 504 is also what ASP.NET Core's
request-timeout middleware answers.

An operation shedding load rather than waiting on a dependency says so:

```csharp
[Timeout(Milliseconds = 2000, Status = 503, RetryAfterSeconds = 30)]
```

`RetryAfterSeconds` is only honest alongside 503. A deadline out at a dependency knows nothing about
when that dependency recovers, so the default sends no header.

A bounded operation publishes its status on the document, so a generated client has a case for the
refusal it will actually be sent rather than a bare transport exception. That is the same mechanism
`[AuthorizeGrants]` and `[RateLimit]` now publish their 403 and 429 through - see
`docs/openapi-document.md`.

The status is written by `ExceptionToModelConverter`, not by the filter. `IOFilter` sits at
`FilterOrder.Serialization`, which is *inside* the filter's span, so by the time the filter regains
control the response has already been serialized from whatever the handler raised. The converter
reads the resolved policy off the handler, so every rung's status reaches the caller, not only an
attribute the handler happens to carry.

**Only where a policy was resolved.** A handler nothing bounds has no deadline to have missed, so a
cancellation there is not described as a gateway timeout: it falls through to the server fault it
was before timeouts existed. That is the same line ASP.NET Core's middleware draws, and it is what
keeps an application that opted into nothing behaving as it did.

**A client disconnect on a bounded handler reads as the deadline's status.** The deadline source is
linked to the transport's token, so both cancel the same way and arrive as the same exception.
Nobody receives either response. The `RequestTimedOut` metric counts only the deadline, so the slow
handler this feature exists to find is not buried under people closing tabs.

## Where the filter runs, and what that buys

One filter per handler, installed by the chain builder from whatever the cascade resolved, at
`FilterOrder.Before + FilterOrder.Serialization`. The attribute installs nothing itself, which is
what makes the cascade expressible: the assembly and entry-point rungs have no attribute on the
handler to provide a filter, and a handler bounded by two rungs would otherwise carry two timers for
one answer.

| Inside the budget | Outside it |
|---|---|
| Parameter binding, validation, resource authorization | The conditional-GET flush |
| Every `[Retry]` attempt, under one budget rather than one each | The response cache's store, and its read |
| The handler | Compression's outward encode |

Anything later is too late: a handler's declared `CancellationToken` parameter is copied out of the
context as the request is bound, so a filter behind serialization hands the handler the transport's
token and the budget reaches nothing at all.

Anything earlier is too loose: `ConditionalGetFilter` flushes the body it held back and
`ResponseCacheFilter` copies its buffer and stores the entry *after* the inner chain returns, both on
`context.CancellationToken`. Dragging those inside the budget means a request that spent all of it
gets its answer flushed and its entry written on an already-cancelled token.

The cost of the position is that the cache's `store.Get` sits outside the budget, so a hanging
distributed store is not bounded by this. That belongs to the store's own timeout.

## What it cannot do

**Cancellation is cooperative.** The budget cancels a token; it does not take a thread back. A
handler that blocks, or that awaits something without passing the token, runs to completion and
answers late.

**A torn stream has no remedy.** Once `IExecutionResponse.ResponseStarted` is true the bytes are with
the client, and a deadline firing mid-stream cuts the body with no status left to send. `RetryFilter`
gives up on the same flag for the same reason. A streaming handler should bound its own work.

**A host has to opt in.** Replacing the token is `IExecutionContext.ReplaceCancellationToken`, a
defaulted interface member whose default refuses. The framework's three contexts override it;
Hardened.Amz's do not yet, so a `[Timeout]` on a Lambda function fails the request with a message
naming the context rather than looking bounded and running forever. A function that declares no
budget is untouched.

That shape is a compatibility decision. A setter on the property would be a breaking change to every
implementation already compiled against the interface — the runtime wants a `set_CancellationToken`
the older assembly does not carry, and the type fails to load on the first request. The template
verification builds this framework against the newest *published* Hardened.Amz on purpose, so that
is the case that has to keep working.

**Lambda has no disconnect either.** Once a context does opt in, the deadline works there, because
the linked source fires on its own timer whatever it links from. What still will not work is a
client hanging up: all three execution contexts in Hardened.Amz seed the token with
`CancellationToken.None`.

## Swapping the token yourself

`CancellationScope` is how, and the restore is why it exists rather than a bare setter:

```csharp
using var deadline = context.WithCancellation(cts.Token);

await chain.Next();
```

Prefer it to calling `ReplaceCancellationToken` directly: a hand-written `finally` is one forgotten
line away from running the rest of a request on a dead token. The framework's own contexts also keep
a public `CancellationToken` setter, so a test driving a request that starts out cancelled can assign
one without going through the scope.

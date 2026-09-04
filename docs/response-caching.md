# Response caching

`[CacheResponse<T>]` stores what a handler answered and serves it again without running the
handler. It is the store half of caching. The header half is `[CacheControl]`, which tells
somebody else to cache and stores nothing; the two are unrelated and compose.

## Two things to reference

Nothing registers a store by default. A store is a DI registration, and a DI registration is
exactly what a trimmer cannot remove, so registering one unconditionally would put the cache and
`Microsoft.Extensions.Caching.Memory` in every application whether or not anything cached.

```xml
<PackageReference Include="Hardened.Requests.Caching.Memory" Version="..." />
```

```csharp
[HardenedModule]
[HardenedWebModule]
[HardenedMemoryResponseCache]
[AspNetCoreRuntime]
public partial class Application { }
```

Without a store, a request to a handler declaring `[CacheResponse]` fails with
`ResponseCacheStoreMissingException` naming the handler, the package to reference and the attribute
to add. The attribute never silently does nothing.

The failure is recorded on the response and the chain continues, which is the rule for everything
ahead of `FilterOrder.Serialization`: the filter that turns a failure into bytes is behind this
stage, so throwing here unwound past it and answered a 500 with `Content-Length: 0`. The caller
gets the framework's error envelope like any other server fault, and the message naming the handler
goes to the log, where an unexpected 500's detail belongs.

## Declaring it

```csharp
[Get("/catalog")]
[CacheResponse<VaryByQuery>("culture", "region", Duration = 60)]
public Catalog Browse([FromQueryString] string culture, [FromQueryString] string region) =>
    _catalog.For(culture, region);
```

The type parameter is the strategy and the positional arguments configure it. Every other
framework names the strategy with a string — ASP.NET Core's `PolicyName`, Spring's
`keyGenerator` — because their containers resolve names at run time. Hardened has none, so a
string here would need a registry built at startup and would fail on the first request that
reached the handler.

`Duration` is in seconds. Omitting it means 60, which is what ASP.NET Core's output cache
defaults to.

`Scope` says who a stored response may be served to, and a handler that requires anything of its
caller has to set it. See [Who the answer is for](#who-the-answer-is-for).

### The strategies in the box

| Strategy | Assembly | Keys on |
|---|---|---|
| `VaryByQuery("a", "b")` | `Hardened.Web.Runtime` | The named query-string values |
| `VaryByHeader("Accept-Language")` | `Hardened.Web.Runtime` | The named request headers, and writes `Vary` |
| `VaryByRoute` | `Hardened.Web.Runtime` | The route's own tokens |
| `ByPayload` | `Hardened.Requests.Runtime` | The whole request body |

Every key is prefixed with the handler's method and path, so two handlers keyed the same way do
not answer each other's requests. `VaryByRoute` on a route with no tokens is therefore a cache of
one entry, which is what a collection endpoint should have.

`VaryByQuery` and `VaryByHeader` take named keys rather than everything. A cache keyed on the whole
query string is one a caller misses at will by adding a parameter nothing reads.

### Writing one

```csharp
public sealed class VaryByTenant : ICacheKeyProvider {
    public static ICacheKeyProvider Create(string[] values) =>
        values.Length == 0
            ? new VaryByTenant()
            : throw new ArgumentException("VaryByTenant takes no values.", nameof(values));

    public ValueTask<string?> Key(IExecutionContext context) =>
        new(context.CallerPrincipal.Subject);
}
```

`Create` is a static abstract interface member, so "constructible from `string[]`" is a real
constraint rather than a convention, and it is reached through the generic parameter rather than by
reflection. It is also where arity is checked: `params string[]` cannot express "this strategy takes
no values", so `[CacheResponse<ByPayload>("culture")]` compiles clean and would otherwise ignore the
argument. Throwing in `Create` turns that into a failure naming the handler as its filter chain is
built.

Returning `null` from `Key` leaves the request neither looked up nor stored.

## Composition

`[CacheResponse<T>]` is `AllowMultiple = true`, and it has to be. The compiler dedupes a generic
attribute on the *unbound* generic rather than the constructed type, so two different type
arguments on one method are `CS0579` without it.

```csharp
[Get("/composed")]
[CacheResponse<VaryByQuery>("culture")]
[CacheResponse<VaryByHeader>("Accept-Language", Duration = 60)]
public Catalog Composed([FromQueryString] string culture) => ...;
```

The parts compose into one key, in the order they were declared, and the handler gets one filter
rather than one per attribute. `[OutputCache]` is `AllowMultiple = false` and cannot compose at
all; you write one policy that does everything.

`Duration` and `Scope` may each appear on more than one attribute. The first that sets one wins,
and two that disagree fail as the chain is built, naming the handler. `Tags` accumulate instead:
every declaration's tags name the one entry, deduped.

## Applying it everywhere

```csharp
services.AddGlobalFilter(
    new CacheResponseAttribute<VaryByRoute> { Duration = 60 },
    when: info => info.Method == "GET");
```

A globally registered instance stands down on any handler that declares `[CacheResponse]` itself,
so explicit beats convention without the registration site saying so.

`AddGlobalFilter` takes any `IRequestFilterProvider`, and the predicate is why it exists rather
than a plain `AddSingleton`: applied to one handler an attribute is read beside the code it
guards, and applied to every handler nobody read anything.

## Who the answer is for

```csharp
[Get("/alerts/{alertId}")]
[Authorize]
[CacheResponse<VaryByRoute>(Duration = 60, Scope = CacheScope.PerCaller)]
public Alert Read(string alertId) => _alerts.OwnedBy(_caller.Principal.Subject, alertId);
```

`CacheScope.PerCaller` puts the caller's issuer and subject in the key, so one caller's answer can
never be handed to another whatever the handler put in it. `CacheScope.AllCallers` is one entry
shared by whoever the guard admits, which is what an authorized read of something public wants.

**A handler that requires anything of its caller and states neither fails, naming the handler.** Not
because the framework cannot pick a default, but because both defaults are wrong. Sharing the entry
leaks one caller's data the moment the answer depends on who asked; keying per caller is safe and
silently turns one shared entry into one per caller, so a cache added to shed load stops shedding
it and grows with the caller count instead.

Nothing on the handler separates the two cases. "Every caller holding `rates:read` gets these same
bytes" and "each caller gets their own" are both authenticated reads with a grant requirement, and
the difference is in what the handler does with the caller. A handler that requires nothing of its
caller states nothing: it has one audience already.

This replaces a rule that read `Requirement.RequiresContext` and claimed a resource-scoped handler
was never cached. That property is true only for a requirement built from `Requirement.Predicate`,
and false for grants and for `Authenticated()` — so it covered a shape almost nobody writes and
missed the two everybody does. An owner-scoped read whose ownership check is handler code answering
404, which is what a description forces because it can require authentication and cannot require
ownership, was cached and served to the next caller.

## Invalidating

```csharp
[Get("/rates/{symbol}")]
[CacheResponse<VaryByRoute>(Duration = 3600, Tags = ["rates"])]
public Rate Read(string symbol) => _rates.Latest(symbol);
```

```csharp
public async Task Publish(RateSet set, CancellationToken cancellationToken) {
    await _rates.Save(set, cancellationToken);
    await _store.EvictByTag("rates", cancellationToken);
}
```

`IResponseCacheStore.EvictByTag` drops every entry stored under the tag. Inject the store where you
change what a cached read reads; without this an application's only way to reach its own entries is
to wait for them to expire, so a publish is visible within the cache lifetime and never sooner.

A tag rather than a key. The key is the handler's `METHOD path`, a unit separator, the caller when
the scope is per-caller, and then each strategy's part — a shape nothing publishes and an
application should not have to rebuild to invalidate its own entries. Composed attributes
contribute to one set of tags, deduped, in the order they were declared.

Group invalidation is the reason this is `IResponseCacheStore` rather than `IDistributedCache`,
which has no atomic operation to do it with. A store that cannot do it atomically should still do
it: entries dropped one at a time is a worse guarantee than all at once, and both are better than a
publish nobody can see.

## Revalidating

```csharp
[Get("/rates/{symbol}")]
[CacheResponse<VaryByRoute>(Duration = 3600, Tags = ["rates"])]
[ConditionalGet]
public Rate Read(string symbol) => _rates.Latest(symbol);
```

```
GET /rates/EUR
HTTP/1.1 200 OK
ETag: "OybX3FuqNfSKoSm+h1FJqQ=="

GET /rates/EUR
If-None-Match: "OybX3FuqNfSKoSm+h1FJqQ=="
HTTP/1.1 304 Not Modified
ETag: "OybX3FuqNfSKoSm+h1FJqQ=="
```

`[ConditionalGet]` answers a GET or HEAD whose caller already holds the response with a 304 and
no body. It goes on an operation or on a class, or on every GET handler in the application as
`[Enable<ConditionalGet>]`, which stands down for a handler that declares its own. Nothing
installs it otherwise. A service whose responses are small and change on every read gets nothing
from a 304, and pays nothing for it.

Every entry the store is handed carries an entity-tag, a SHA-256 of the bytes stored, computed as
the response is captured when the handler wrote none. It goes out with the miss and is replayed
with the hit, and it is what the filter compares against: a hit is answered 304 without running
the handler and without the stored body. The filter sits outside the cache, which is the ordering
`FilterOrder.Conditional` was reserved for, and outside compression, so a 304 carries no coding
for a body it does not have. The tag is strong, because a hit writes the stored bytes byte for
byte; the compression filter weakens it as it encodes, so a client that accepts gzip holds
`W/"..."`, and `If-None-Match` compares weakly either way.

### What it costs

The filter decides on the first write. A response that already carries an `ETag` by then - the
cache tagged the entry, a handler wrote one - is decided there and then, a 304 or the bytes
straight through to the transport. A response carrying none is held back and tagged over the
bytes as sent once they are all there: a buffer and a hash per response. That is the cost of
declaring this on a handler that neither caches nor writes a validator, and it buys bandwidth
only. The handler ran, and a 304 from a hash of its output saved the transfer and the client's
parse, not the work. It is worth having for a large or frequently polled response, and for a
shared cache in front of the service, which revalidates when its copy expires and keeps it on a
304. It is not worth having for a small response that changes on every read, which is why it is
declared rather than assumed.

The tag covers the bytes as sent. Behind the compression filter a gzip client and an identity
client hold different tags for one resource, as they do for a compressed static file, and each
is revalidated against its own.

Do not declare it on a handler returning `IAsyncEnumerable<T>`, for the reason given for the
cache below: holding a stream back is buffering it.

### A handler's own validator

A handler that knows its resource's version writes it, and is passed straight through rather
than held back:

```csharp
[Get("/documents/{id}")]
[ConditionalGet]
public Document Read(string id, IExecutionContext context) {
    var document = _documents.Find(id);

    context.Response.Headers[KnownHeaders.ETag] = "\"" + document.Version + "\"";
    context.Response.Headers[KnownHeaders.LastModified] = HttpDate.Format(document.UpdatedAt);

    return document;
}
```

`If-None-Match` is evaluated when it is present and `If-Modified-Since` only when it is not,
including when the tag does not match. That is RFC 9110 §13.2.1's order, implemented once in
`Precondition` and shared with static content. A handler's own tag is kept when the response is
also cached: a version that changes when the resource does says more than a hash of the
serializer's output.

This costs the body, not the handler. A handler that wrote its own validator ran in order to
write it. Skipping the work needs the validator before the handler runs, which is not built.

### What a 304 carries

The status, and the headers RFC 9110 §15.4.5 says a 304 repeats when a 200 would have sent them:
`ETag`, `Last-Modified`, `Cache-Control`, `Vary` and whatever else the handler and the filters
wrote. `Content-Type`, `Content-Length` and `Content-Encoding` are removed, because they describe
content the response does not have, and a HEAD answered 304 reports no length for the same reason.

A 304 stands in for a 200 and nothing else. A 404 or a 500 is sent as it is, and so is a refusal:
authorization and rate limiting record theirs ahead of this stage and the filter reads what they
recorded, so a caller who may not read the resource is not told that it has not changed.

## What is not cached

**A handler whose authorization reads the request.** The filter runs at
`FilterOrder.ResponseCache`, which is after authorization over grants alone and before
authorization that reads bound parameters — so such a requirement does not run on a hit at all, and
keying per caller would not make it run. The filter is not installed, decided once per handler from
`Requirement.RequiresContext`.

ASP.NET Core ships the same hazard as a documentation note telling you to call `UseOutputCache`
after `UseAuthorization`, and the failure is silent.

**A request something already refused.** Authorization and rate limiting sit ahead of this stage
and refuse by recording the failure and continuing, because the filter that writes a refusal is
behind them. So a refused request arrives here still travelling, and the cache reads what they
recorded rather than treating that as permission. It is neither answered from the store nor stored.

**Anything that is not a 200.** A 404 or a 500 is about the moment rather than the resource, and a
redirect or a 304 carries its meaning in headers this does not model.

## What headers a hit carries

A stored entry holds what this representation is, not what its first request was. Three kinds of
header are dropped as a response is captured — not as one is replayed, so a store written by an
older build cannot leak one either:

| Dropped | Why |
|---|---|
| `Set-Cookie` | Belongs to a caller. Replaying one hands a second caller the first one's session. |
| `Transfer-Encoding`, `Connection`, `Keep-Alive`, `TE`, `Trailer`, `Upgrade`, `Proxy-Authenticate`, `Proxy-Authorization`, `Content-Length`, `Date`, `Server` | Belong to a connection or to a moment. A host frames a body with no `Content-Length` as chunked and says so on the response; an entry that kept that header re-declared chunked framing on the hit and then wrote the stored bytes unframed, which is a malformed response on every socket. |
| Anything the response already carried on the way in | Belongs to this request. Whatever wrote it sits ahead of this stage and runs on a hit as well, so its own value is already there — and storing one froze the first caller's `X-Correlation-Id`, and the first request's `RateLimit-Remaining`, onto everyone else for the whole duration. A header the chain *changed* is kept, because a miss would have changed it the same way. |

What is left is what the handler and the filters inside the cache produced, which is what carries
`Cache-Control` and `ETag` onto a hit - the handler's own tag, or the one the capture computed
when it wrote none (see [Revalidating](#revalidating)). `Vary` reaches a hit because
`VaryByHeader` writes it while composing the key, on every request.

## What it stores

Bytes, not the value the handler returned. The point is to skip the serialize as well as the
handler, and a stored model would have to be serialized again on every hit.

The consequence is that a stored entry is one *representation*. Content negotiation happened on the
request that filled the cache, so a key must include whatever the response varies on — add
`VaryByHeader("Accept")` to a handler that answers more than one media type.

## On Lambda

Nothing eliminates the invoke: the request is still billed for its duration, so this is a
downstream cache rather than a CDN. It is worth having when the handler's work is a DynamoDB query
or an external call, and worth nothing when the handler is cheap.

`ByPayload` is the strategy for a directly invoked function. IAM authorizes at the boundary and
`ILambdaContext` carries no caller principal, so a function cannot vary its answer by caller — and
the moment a caller does pass a tenant or a user id, it is in the payload.

`MemoryCache` is not timer-driven in .NET 8 and checks expiry on read, so the store stays correct
across a freeze of any length; only reclamation happens late. The in-process store decides validity
on its own `TimeProvider` as well, so a frozen environment and a test both see the same rule.

## Configuration

```csharp
services.ConfigureMemoryResponseCache(cache => {
    cache.SizeLimit = 32 * 1024 * 1024;
    cache.MaximumBodySize = 1024 * 1024;
});
```

Defaults are 100 MB total and 64 MB per entry, matching ASP.NET Core. The limits are set here
rather than on `[HardenedMemoryResponseCache]` because DependencyModules unwraps `Nullable<T>` when
it generates a module attribute, so a `long?` becomes a `long` and would be copied onto the module
as `0` for anyone who wrote the attribute with no arguments.

## Testing a duration

`[HardenedMemoryResponseCache]` registers `TimeProvider.System` with `TryAddSingleton`, and the
in-process store decides on it whether an entry is still valid. Register a `TimeProvider` of your
own and a test moves time instead of waiting for it:

```csharp
services.AddSingleton<TimeProvider>(clock);   // before the module, or it keeps yours either way

await app.Get("/rates/EUR");
clock.Advance(TimeSpan.FromHours(2));         // a day-long entry, tested in a millisecond
```

`MemoryCache`'s own absolute expiration is still set and still runs on the machine clock. That one
decides when the memory is freed, not what a request is answered with.

## Not built

- **Per-resource invalidation.** A tag names a handler's entries, not a row: "drop every cached
  response touching pet 7" needs the tag to carry the id, which means the declaration cannot be a
  constant. ASP.NET Core has the same shape and the same limit.
- **Stampede protection.** ASP.NET Core locks per resource so a cold key is computed once. Here,
  *n* concurrent misses run the handler *n* times and the last one wins.
- **Skipping the handler on a validator it knows.** A 304 from a handler's own `ETag` costs the
  body, not the handler: the handler ran to write the tag. Answering before it runs needs the
  current validator from somewhere else. Only a cache hit skips the handler today.
- **`If-Match` and `If-Unmodified-Since`.** They guard a write against a lost update and answer
  412, which needs the current validator before the handler runs for the same reason.
  `KnownHeaders.IfMatch` remains a constant with no implementation.

## One thing to avoid

Do not put `[CacheResponse]` on a handler returning `IAsyncEnumerable<T>`. Capturing a response
means buffering it, and buffering a stream defeats the point of streaming it and holds the whole
sequence in memory first. Nothing refuses this yet: whether a handler streams is decided by the
generator picking `AsyncEnumerableIoFilter`, and it is not on
`IExecutionRequestHandlerInfo`, so the filter cannot read it the way it reads `Requirement`.

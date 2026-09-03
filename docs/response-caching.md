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
`Cache-Control` and `ETag` onto a hit. `Vary` reaches a hit because `VaryByHeader` writes it while
composing the key, on every request.

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
- **Conditional GET.** `EntityTagHeader` can format and weakly compare validators and nothing reads
  `If-None-Match` back for a handler, so a client that revalidates still gets a full body.
  `KnownHeaders.IfMatch` remains a constant with no implementation.

## One thing to avoid

Do not put `[CacheResponse]` on a handler returning `IAsyncEnumerable<T>`. Capturing a response
means buffering it, and buffering a stream defeats the point of streaming it and holds the whole
sequence in memory first. Nothing refuses this yet: whether a handler streams is decided by the
generator picking `AsyncEnumerableIoFilter`, and it is not on
`IExecutionRequestHandlerInfo`, so the filter cannot read it the way it reads `Requirement`.

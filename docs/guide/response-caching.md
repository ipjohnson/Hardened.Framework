# Response caching

A handler that answers the same bytes to every caller for the next minute should not run a
thousand times to do it. `[CacheResponse<T>]` stores what the handler answered and serves it
again without running the handler, without binding the request and without serializing anything.

```csharp
[Get("/catalog")]
[CacheResponse<VaryByQuery>("culture", "region", Duration = 60)]
public Catalog Browse([FromQueryString] string culture, [FromQueryString] string region) =>
    _catalog.For(culture, region);
```

This is the store half of caching. The header half is
[`[CacheControl]`](/guide/routing#caching), which tells somebody else to cache and stores
nothing. The two compose.

## Turning it on

A store is a package and an attribute on the module:

```xml
<PackageReference Include="Hardened.Requests.Caching.Memory" Version="0.20.0-rc1000" />
```

```csharp
[HardenedModule]
[HardenedWebModule]
[HardenedMemoryResponseCache]
[KestrelRuntime]
public partial class Application { }
```

Nothing registers a store by default, so an application that caches nothing does not carry the
cache. Without a store, the first request to a handler declaring `[CacheResponse]` answers the
framework's error envelope, and the log names the handler, the package to reference and the
attribute to add.

## Pick what the key is

The type parameter is the strategy. The positional arguments configure it:

| Strategy | From | Keys on |
|---|---|---|
| `VaryByQuery("culture", "region")` | `Hardened.Web.Runtime` | The named query-string values |
| `VaryByHeader("Accept-Language")` | `Hardened.Web.Runtime` | The named request headers, and writes `Vary` |
| `VaryByRoute` | `Hardened.Web.Runtime` | The route's own tokens |
| `ByPayload` | `Hardened.Requests.Runtime` | The whole request body |

Every key is prefixed with the handler's method and path, so two handlers keyed the same way
never answer each other's requests. `VaryByRoute` on a route with no tokens is a cache of one
entry, which is what a collection endpoint wants.

`VaryByQuery` and `VaryByHeader` take named keys rather than everything. A cache keyed on the
whole query string is one a caller misses at will by adding a parameter nothing reads.

`Duration` is in seconds. Omitting it means 60.

### Writing a strategy

`ICacheKeyProvider` is an interface anyone can implement:

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

`Create` is a static abstract interface member, called once per handler as the filter chain is
built. That is also where arity is checked: `[CacheResponse<ByPayload>("culture")]` compiles, so
`Create` throwing is what turns the stray argument into a startup failure naming the handler.
Returning `null` from `Key` leaves the request neither looked up nor stored.

### Composing two

`[CacheResponse<T>]` is `AllowMultiple`, and two different type arguments on one method need it:

```csharp
[Get("/composed")]
[CacheResponse<VaryByQuery>("culture")]
[CacheResponse<VaryByHeader>("Accept-Language", Duration = 60)]
public Catalog Composed([FromQueryString] string culture) => ...;
```

The parts compose into one key, in the order they were declared, and the handler gets one filter.
`Duration` and `Scope` may each appear on more than one attribute: the first that sets one wins,
and two that disagree fail as the chain is built. `Tags` accumulate, deduped.

## Say who the answer is for

```csharp
[Get("/alerts/{alertId}")]
[Authorize<BearerAuth>]
[CacheResponse<VaryByRoute>(Duration = 60, Scope = CacheScope.PerCaller)]
public Alert Read(string alertId) => _alerts.OwnedBy(_caller.Principal.Subject, alertId);
```

`CacheScope.PerCaller` puts the caller's issuer and subject in the key, so one caller's answer is
never handed to another. `CacheScope.AllCallers` is one entry shared by whoever the guard admits,
for an authorized read of something public.

A handler that requires anything of its caller and states neither fails as its filter chain is
built, naming the handler. Sharing the entry would leak one caller's data the moment the answer
depends on who asked, and keying per caller would silently turn one shared entry into one per
caller. Nothing on the handler tells the two apart, so the handler has to say. A handler that
requires nothing of its caller states nothing.

A caller with no subject is never cached under `PerCaller`.

## Invalidate by tag

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

Inject `IResponseCacheStore` where you change what a cached read reads. `EvictByTag` drops every
entry stored under the tag, and a tag nothing was stored under is not an error. Without a tag, an
application's only way to reach its own entries is to wait for them to expire.

## What is not cached

A handler whose authorization reads the request. The filter runs at `FilterOrder.ResponseCache`,
after authorization over grants alone and before authorization that reads bound parameters. Such
a requirement would not run on a hit, so the filter is not installed, decided once per handler.

A request something already refused. Authorization and rate limiting sit ahead of this stage and
refuse by recording the failure and continuing. The cache reads what they recorded, so the
request is neither answered from the store nor stored.

Anything that is not a 200. A 404 or a 500 is about the moment rather than the resource. A
redirect or a 304 carries its meaning in headers this does not model.

## What a hit carries

A stored entry holds what the representation is, not what its first request was. Three kinds of
header are dropped as a response is captured:

| Dropped | Why |
|---|---|
| `Set-Cookie` | Belongs to a caller. Replaying one hands a second caller the first one's session |
| `Transfer-Encoding`, `Connection`, `Keep-Alive`, `TE`, `Trailer`, `Upgrade`, `Proxy-Authenticate`, `Proxy-Authorization`, `Content-Length`, `Date`, `Server` | Belong to a connection or to a moment |
| Anything the response already carried on the way in | Belongs to this request. Whatever wrote it sits ahead of this stage and runs on a hit as well, so its own value is already there. A header the chain changed is kept |

What is left is what the handler and the filters inside the cache produced, which is what carries
`Cache-Control` and `ETag` onto a hit. `Vary` reaches a hit because `VaryByHeader` writes it while
composing the key, on every request.

## What it stores

Bytes, not the value the handler returned, so a hit skips the serializer as well as the handler.
A stored entry is therefore one representation. Content negotiation happened on the request that
filled the cache, so the key has to include whatever the response varies on. Add
`VaryByHeader("Accept")` to a handler that answers more than one media type.

## Apply it everywhere

```csharp
services.AddGlobalFilter(
    new CacheResponseAttribute<VaryByRoute> { Duration = 60 },
    when: info => info.Method == "GET");
```

A globally registered instance stands down on any handler that declares `[CacheResponse]`
itself, so explicit beats convention.

## Configuration

```csharp
services.ConfigureMemoryResponseCache(cache => {
    cache.SizeLimit = 32 * 1024 * 1024;
    cache.MaximumBodySize = 1024 * 1024;
});
```

Defaults are 100 MB total and 64 MB per entry.

## Testing a duration

`[HardenedMemoryResponseCache]` registers `TimeProvider.System` with `TryAddSingleton`, and the
in-process store decides on it whether an entry is still valid. Register a `TimeProvider` of your
own and a test moves time instead of waiting for it:

```csharp
private sealed class TestClock : TimeProvider {
    private DateTimeOffset _now = new(2026, 9, 3, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
```

```csharp
services.AddSingleton<TimeProvider>(clock);

await app.Get("/rates/EUR");
clock.Advance(TimeSpan.FromHours(2));
```

A day-long entry is testable in a millisecond. `FakeTimeProvider` from
`Microsoft.Extensions.TimeProvider.Testing` does the same job if you already reference it.
[Substituting services](/guide/testing-mocks#a-fake-instead-of-a-mock) shows the registration as
a test attribute.

`MemoryCache`'s own absolute expiration is still set and still runs on the machine clock. That
one decides when the memory is freed, not what a request is answered with.

## On Lambda

Nothing eliminates the invoke, so this is a downstream cache rather than a CDN. It is worth
having when the handler's work is a DynamoDB query or an external call, and worth nothing when
the handler is cheap.

`ByPayload` is the strategy for a directly invoked function. IAM authorizes at the boundary and
`ILambdaContext` carries no caller principal, so a function cannot vary its answer by caller.
The moment a caller does pass a tenant or a user id, it is in the payload.

`MemoryCache` is not timer-driven in .NET 8 and checks expiry on read, so the store stays correct
across a freeze of any length. Only reclamation happens late.

## Revalidating a hit

A cached handler that also declares [`[ConditionalGet]`](/guide/conditional-requests) answers a
caller who already holds the response with a 304 and no body, without running the handler and
without sending the stored bytes. Every entry the store is handed carries an entity-tag, computed
as the response is captured when the handler wrote none, so the tag goes out with the miss and is
replayed with the hit.

## Not built

- **Per-resource invalidation.** A tag names a handler's entries, not a row. "Drop every cached
  response touching pet 7" needs the tag to carry the id, which means the declaration cannot be a
  constant.
- **Stampede protection.** *n* concurrent misses run the handler *n* times and the last one wins.

::: danger Do not cache a stream
`[CacheResponse]` on a handler returning `IAsyncEnumerable<T>` buffers the whole sequence in
memory before storing it. Nothing refuses this yet.
:::

## Next

- [Conditional requests](/guide/conditional-requests): a 304 for a caller holding the response
- [Compression](/guide/compression): a hit is encoded on the way out
- [Substituting services](/guide/testing-mocks#a-fake-instead-of-a-mock): the clock in a test

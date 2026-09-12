# Response caching

`[CacheResponse<T>]` stores the bytes a handler answered. A later request with the same key gets
those bytes, and the handler does not run.

```csharp
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Runtime.Caching;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Caching;

public class CatalogController {

    [Get("/catalog")]
    [CacheResponse<VaryByQuery>("culture", "region", Duration = 60)]
    public string Browse([FromQueryString] string culture, [FromQueryString] string region) =>
        _catalog.For(culture, region);
}
```

The type argument names the key strategy. The positional arguments configure that strategy.

`[CacheControl]` is a different attribute. It writes a `Cache-Control` header and stores nothing.
See [Routing](/guide/routing).

## The attribute

| Member | Type | Default | What it does |
| --- | --- | --- | --- |
| `TProvider` | type argument | none | Names the key strategy. |
| positional values | `params string[]` | empty | The strategy's arguments, such as query keys. |
| `Duration` | `int` | `0`, which means 60 seconds | How long the store keeps the entry, in seconds. |
| `Scope` | `CacheScope` | `CacheScope.Unstated` | Who the stored answer is served to. |
| `Tags` | `string[]` | empty | The names that evict this entry. |

The attribute applies to a method or to a class. `AllowMultiple` is true.

## The store

Nothing registers a store by default. Reference the `Hardened.Requests.Caching.Memory` package.
Write `[HardenedMemoryResponseCache]` on the application module.

```csharp
[HardenedModule]
[HardenedWebModule]
[HardenedMemoryResponseCache]
[AspNetCoreRuntime]
public partial class Application { }
```

This repository ships one store, the in-process one. `IResponseCacheStore` is the seam for another.
An application that registers its own implementation does not need the module attribute.

### The module the attribute goes on

Put `[HardenedMemoryResponseCache]` on the library module, beside the handlers. The web template
splits an application into a library module and a host module. The host carries the runtime
attribute and imports the library.

A test boots the library module alone. The template writes
`[assembly: HardenedTestEntryPoint(typeof(TemplateModuleNameLibrary))]`. A store on the host module
is therefore absent in every test. Each cached handler then answers the no-store failure.

The build says nothing about that mistake. `HRDW005` is asked of the host, and the host registers a
store. A library module that carries the attribute registers the store for every host that imports
it.

### The build warning

The generator reports `HRDW005` when an application declares caching and applies no store module.
The severity is Warning. The message is:

```text
'{handlers}' declares [CacheResponse] and this application registers no response cache store, so
every request to it answers an error. Add the Hardened.Requests.Caching.Memory package and
[HardenedMemoryResponseCache] to the module, or register an IResponseCacheStore yourself and
suppress HRDW005.
```

One report covers the whole assembly. The message names every handler that fails.

The generator asks this question of the compilation whose entry point applies a web runtime. A
library that declares cached handlers reports nothing. The host that imports the library is told
which of its handlers cache. A library that applies `[HardenedMemoryResponseCache]` itself
registers the store for every host that imports it.

The check reads module attributes only. A store registered by hand in `ConfigureServices` is
invisible to it. That is why the report is a warning and not an error.

### The run-time failure

A cached handler in an application with no store answers 500 and the error envelope. The filter
records `ResponseCacheStoreMissingException` on the response and calls the next filter. The message
reaches the log:

```text
GET /catalog declares [CacheResponse] and no IResponseCacheStore is registered. Reference
Hardened.Requests.Caching.Memory and add [HardenedMemoryResponseCache] to the application module,
or register a store of your own.
```

The failure arrives on a request rather than at startup. Hardened builds a handler on the first
request its route matches.

## The cache key

The filter composes one key from the handler and from every strategy on it. The parts are joined
with the unit separator, `U+001F`. The parts appear in this order.

1. The handler's method and path, as `GET /catalog`.
2. The caller's issuer and subject, when the scope is `CacheScope.PerCaller`.
3. Each strategy's part, in the order the attributes were declared.

Two handlers keyed the same way never answer each other's requests. The method and path sit in
front of every key.

| Strategy | Namespace | Arguments | Key part |
| --- | --- | --- | --- |
| `VaryByRoute` | `Hardened.Web.Runtime.Caching` | none | `name=value&` for every route token |
| `VaryByQuery` | `Hardened.Web.Runtime.Caching` | one or more query keys | `name=value&` for each named key |
| `VaryByHeader` | `Hardened.Web.Runtime.Caching` | one or more header names | `Name=value&` for each named header |
| `ByPayload` | `Hardened.Requests.Runtime.Caching` | none | the SHA-256 of the body, base64 |

`VaryByQuery` reads only the keys it was named. A caller who adds another parameter still hits the
same entry. An absent key reads as empty.

`VaryByHeader` also writes the response's `Vary` header. It adds each name it varies on, and keeps
the names already there. It reads a request header whatever the case of the name. It refuses
`Cookie`, because a response keyed on a session has one caller.

`VaryByRoute` reads every token the route declares, bound or not. A route with no token keys as
empty. That is one entry for the route.

`ByPayload` reads the whole request body. It runs ahead of the bind, so it buffers the body, hashes
it, and puts the buffer back. A request with no body keys as empty.

Each strategy refuses arguments it cannot use. `VaryByRoute` and `ByPayload` take no values.
`VaryByQuery` and `VaryByHeader` need at least one. The failure names the handler and wraps the
strategy's own message:

```text
[CacheResponse] on GET /catalog could not build its cache key strategy: VaryByRoute keys on the
route's own tokens and takes no values, but was given culture. Name query keys with VaryByQuery
instead.
```

### A strategy of your own

Implement `ICacheKeyProvider`. `Create` builds the strategy once per handler from the attribute's
positional arguments. `Key` answers what this request is stored under.

```csharp
public sealed class VaryByTenant : ICacheKeyProvider {

    public static ICacheKeyProvider Create(string[] values) => new VaryByTenant();

    public ValueTask<string?> Key(IExecutionContext context) =>
        new(context.CallerPrincipal.Subject);
}
```

A null key leaves the request uncached. The filter neither reads the store nor writes to it.
`Create` may throw on an argument the strategy cannot use. That failure names the handler.

### Two strategies on one handler

Write the attribute twice. Both parts go into one key, and one filter serves the handler.

```csharp
[Get("/composed")]
[CacheResponse<VaryByQuery>("culture")]
[CacheResponse<VaryByHeader>("Accept-Language", Duration = 60)]
public string Composed([FromQueryString] string culture) => _catalog.For(culture);
```

A null from any one strategy leaves the whole request uncached. There is no partial key.

Composed attributes share one entry, so they share one lifetime and one audience. The first
declared `Duration` wins. Two that disagree fail as the chain is built:

```text
GET /composed declares [CacheResponse] twice with different durations, 300 and 60. Composed
attributes share one lifetime, so set Duration on one of them.
```

Two scopes that disagree fail the same way. Tags from every declaration form one set, and a tag
two of them name is one tag.

A `[CacheResponse<T>]` registered through `AddGlobalFilter` applies to a handler that declares none
of its own. The global one does not apply to a handler that declares its own.

```csharp
services.AddGlobalFilter(
    new CacheResponseAttribute<VaryByRoute> { Duration = 60 },
    when: info => info.Method == "GET");
```

### A declaration on the module

`[CacheResponse<T>]` on a `[HardenedModule]` class covers every handler in the same compilation.
That is how an application caches by convention. The generator emits the declaration once beside
the routing table. Each handler's chain merges it into that handler's metadata.

The merge drops a declaration whose closed type the handler already carries. `VaryByRoute` and
`VaryByQuery` close the attribute differently, so they are two types. A handler declaring
`[CacheResponse<VaryByQuery>]` under a module declaring `[CacheResponse<VaryByRoute>]` gets both.
The two compose into one key. Two that disagree on `Duration` or on `Scope` fail as the chain is
built.

A module declaration stands down only for the identical closed type. `AddGlobalFilter` is the
looser rule. There, any declaration on the handler stands the global one down.

## Cache scope

`CacheScope` states who a stored answer may be served to.

| Value | What it means |
| --- | --- |
| `Unstated` | The declaration said nothing. This is the default. |
| `AllCallers` | One entry, served to every caller the guard admits. |
| `PerCaller` | One entry for each caller. The issuer and the subject go into the key. |

A handler that requires nothing of its caller needs no scope. `Unstated` becomes `AllCallers`.

A handler with an authorization requirement must state a scope. `Unstated` on such a handler throws
`CacheScopeUndeclaredException` as the filter chain is built. The first request to that route
answers 500 and the error envelope:

```text
GET /catalog requires {requirement} of its caller and declares [CacheResponse] without saying who a
stored response may be served to. Set Scope = CacheScope.PerCaller if the answer depends on who
asked - an owner-scoped read, anything filtered by the caller's tenant - or
Scope = CacheScope.AllCallers if every caller the guard admits gets the same bytes.
```

The exception carries the handler on its `Handler` property, as `GET /catalog`.

Use `PerCaller` when the handler's own code narrows the answer to the caller. An ownership check
inside the handler is invisible to the framework. Nothing on the handler tells that case apart from
a shared read.

```csharp
[Get("/owned-by-subject")]
[AuthorizeGrants("pets:read")]
[CacheResponse<VaryByRoute>(Duration = 60, Scope = CacheScope.PerCaller)]
public string OwnedBySubject() => _rows.For(_currentCaller.Principal.Subject);
```

`PerCaller` keys on the issuer as well as the subject. Two issuers naming one subject are two
callers. A request from a caller with no subject is neither looked up nor stored.

## Handlers that are not cached

The filter runs at `FilterOrder.ResponseCache`. That position is after
`FilterOrder.GrantAuthorization` and before `FilterOrder.Authorization`.

A requirement that reads the request runs after the cache would have answered.
`Requirement.Predicate` sets `RequiresContext`, and the attribute then installs no filter at all.
`Requirement.Grant` and `Requirement.Authenticated` do not set it, so those handlers are cached.

A refused request is neither answered from the store nor stored. Every filter ahead of
`FilterOrder.Serialization` refuses by recording the failure and calling the next filter. The cache
reads `IExecutionResponse.Refused`, which is true once `ExceptionValue` is set. A caller the
authorization filter turned away gets the refusal, not the stored body.

The filter reads no request method. It stores the answer of a handler on any verb.

Nothing refuses a handler that streams. Whether a handler returns `IAsyncEnumerable<T>` is not on
`IExecutionRequestHandlerInfo`. Capturing a response buffers it, so such a handler holds its whole
sequence in memory.

## Duration

`Duration` is a whole number of seconds. `0` means `ResponseCacheFilter.DefaultDuration`, which is
60 seconds.

The entry expires at a fixed point after the store took it. A hit does not extend it. The store
checks the expiry on every read, and drops an entry it finds expired.

## Invalidation by tag

`Tags` names an entry. `IResponseCacheStore.EvictByTag` drops every entry under one name.

```csharp
[Get("/tagged")]
[CacheResponse<VaryByRoute>(Duration = 3600, Tags = ["catalog"])]
public string Tagged() => _catalog.Read();

[Post("/publish")]
public async Task<string> Publish(CancellationToken cancellationToken) {
    await _store.EvictByTag("catalog", cancellationToken);

    return "published";
}
```

Inject `IResponseCacheStore` into the handler that changes the data a cached read returns. A tag
nothing was stored under is not an error.

`EvictByTag` is the only way an application reaches its own entries. There is no removal by key.
The key is composed from the handler, the caller and each strategy, and nothing publishes that
shape.

## The stored entry

`CachedResponse` holds the status, the content type, the body bytes, some response headers, and the
tags. `Size` is the body length. Headers do not count towards it.

Only a 200 is stored. A 302, a 304, a 404 and a 500 are not. A refused response is not.

These response headers never reach an entry, whatever wrote them.

| Headers | Reason |
| --- | --- |
| `Set-Cookie` | It belongs to one caller. |
| `Transfer-Encoding`, `Content-Length`, `Connection`, `Keep-Alive`, `TE`, `Trailer`, `Upgrade`, `Proxy-Authenticate`, `Proxy-Authorization` | The transport frames each response as it writes it. |
| `Content-Encoding` | The entry holds identity bytes. |
| `Date`, `Server` | They belong to the host and to the moment. |

A header the response already carried before the cache filter ran is not stored either. The filter
that wrote it sits ahead of the cache and writes it again on a hit. A correlation id and a rate
limit header therefore carry the current request's value. A header the inner chain changed is
stored, because a miss would change it the same way.

An entry is one representation. It holds one content type and one body. The serializer runs at
`FilterOrder.Serialization`, which is inside the cache, so a hit negotiates nothing. The first
request settled the media type for every hit after it.

A handler that declares several media types with `[Produces]` must key on what it varies by. Add
`[CacheResponse<VaryByHeader>("Accept")]`. Without it a client asking for the second media type
gets the first one's bytes.

The filter puts a strong entity tag on a stored response that carries none. The tag is the SHA-256
of the stored bytes, base64, in quotes. A handler that wrote its own `ETag` keeps it. A response
that is not stored gets no tag.

## A served hit

A hit sets the stored status, the stored headers and the stored content type, then writes the
stored bytes. No header marks a response as a hit.

The filter returns without calling the next filter, so the bind, the handler and the serializer do
not run. Filters ordered ahead of `FilterOrder.ResponseCache` still run, on the way in and on the
way out.

| Filter | Order | On a hit |
| --- | --- | --- |
| `[ConditionalGet]` | `FilterOrder.Conditional` | Runs. Answers 304 against the replayed `ETag`. |
| `[Compress]` | `FilterOrder.Before + FilterOrder.ResponseCache` | Runs. Encodes the stored bytes on the way out. |
| `[CacheControl]` | `FilterOrder.BeforeSerialization` | Does not run. Its header is replayed with the rest. |

The compression filter does not consult its predicate on a hit. A hit carries no handler value, so
the media-type rule decides instead.

Concurrent misses on one key all run the handler. The filter holds no lock and takes no permit. The
last request to finish writes the entry that stays.

## The in-memory store

`MemoryResponseCacheStore` owns its own `MemoryCache`. It does not use the application's
`IMemoryCache`.

| Setting | Default |
| --- | --- |
| `SizeLimit` | 104857600 bytes, which is 100 MB |
| `MaximumBodySize` | 67108864 bytes, which is 64 MB |

A response over `MaximumBodySize` is not stored. The store refuses by doing nothing. The response
already reached the client, so the only consequence is that the next request misses.

Set the limits with `ConfigureMemoryResponseCache`. It amends the configuration, so two calls both
apply.

```csharp
public void ConfigureServices(IServiceCollection services) {
    services.ConfigureMemoryResponseCache(cache => {
        cache.SizeLimit = 32 * 1024 * 1024;
        cache.MaximumBodySize = 1024 * 1024;
    });
}
```

The module registers the store with `TryAddSingleton`. An `IResponseCacheStore` already in the
collection wins, whichever order the modules were listed in.

## The store clock

The store decides expiry on the `TimeProvider` it was given. A test moves the clock instead of
waiting out a duration.

```csharp
private sealed class TestClock : TimeProvider {
    private DateTimeOffset _now = new(2026, 9, 3, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
```

`[HardenedMemoryResponseCache]` registers `TimeProvider.System` with `TryAddSingleton`. A
`TimeProvider` already in the collection is kept.

`MemoryCache` still holds an absolute expiry on the machine clock. That one only decides when the
memory is freed.

## AWS Lambda

`[HardenedMemoryResponseCache]` keeps this process's answers. Each execution environment holds its
own entries and its own tag index. `EvictByTag` reaches the environment it ran in.

A hit does not avoid the invoke. The request still runs and is still billed for its duration. This
is a downstream cache and not a CDN.

`MemoryCache` in .NET 8 checks expiry when it reads an entry. Its scan is not timer-driven. A
freeze of any length therefore does not produce a stale hit. Only reclamation happens late.

`ByPayload` is the strategy for a function handler. A directly invoked Lambda carries no caller
principal on `ILambdaContext`, so a function cannot vary its answer by caller. Whatever a caller
passes is in the payload.

```csharp
[HardenedFunction]
[CacheResponse<ByPayload>(Duration = 300)]
public Quote Price(QuoteRequest request) => _pricing.Quote(request);
```

## Next

- [Conditional requests](/guide/conditional-requests)
- [Compression](/guide/compression)
- [Execution pipeline](/guide/execution-pipeline)
- [Diagnostics](/reference/diagnostics)

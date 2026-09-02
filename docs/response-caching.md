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

Without a store, the first request to a handler declaring `[CacheResponse]` fails with
`ResponseCacheStoreMissingException` naming the handler. The attribute never silently does
nothing.

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

`Duration` may appear on more than one attribute. The first that sets one wins, and two that
disagree fail as the chain is built, naming the handler.

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

## What is not cached

**A handler whose authorization reads the request.** The filter runs at
`FilterOrder.BeforeSerialization`, which is after authorization over grants alone and before
authorization that reads bound parameters. A stored answer to "may this caller edit *this* pet"
served to a second caller is a data leak, so the filter is not installed at all — decided once per
handler from `IExecutionRequestHandlerInfo.Requirement.RequiresContext`. A requirement over grants
alone settles ahead of the cache and is cached normally.

ASP.NET Core ships the same hazard as a documentation note telling you to call `UseOutputCache`
after `UseAuthorization`, and the failure is silent.

**Anything that is not a 200.** A 404 or a 500 is about the moment rather than the resource, and a
redirect or a 304 carries its meaning in headers this does not model.

**`Set-Cookie`.** Stripped as a response is captured, not as one is replayed, so a store written by
an older build cannot leak one either. Every other response header is replayed as it was sent,
which is what carries `Cache-Control`, `ETag` and `Vary` onto a hit.

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
across a freeze of any length; only reclamation happens late.

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

## Not built

- **Tag-based invalidation.** Nothing can say "drop every cached response touching pet 7". ASP.NET
  Core has `Tags` and `IOutputCacheStore.EvictByTagAsync`; Symfony pushes tags out to Varnish,
  Fastly and Cloudflare. It is orthogonal to the key design and is the next thing to decide.
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

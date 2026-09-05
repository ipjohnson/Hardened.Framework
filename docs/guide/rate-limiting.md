# Rate limiting

`[RateLimit]` caps how often a handler may be called.

```csharp
[Post("/tokens")]
[RateLimit(PermitLimit = 10, WindowSeconds = 60)]
public Task<Token> Issue(Credentials credentials) => _tokens.Issue(credentials);
```

```
POST /tokens
HTTP/1.1 429 Too Many Requests
Retry-After: 42
```

A caller over the limit gets 429 with `Retry-After`. Every allowed request carries the
`RateLimit-Limit`, `RateLimit-Remaining` and `RateLimit-Reset` headers, so a client can pace
itself rather than discover the limit by hitting it.

| | Default |
|---|---|
| `PermitLimit` | 100 |
| `WindowSeconds` | 60 |
| `Name` | `"default"` |
| `Scope` | `RateLimitScope.Transport` |

## Turning it on

The attribute is the declaration. The counting needs a store, and `InProcessRateLimitStore` is
registered for you, so a handler carrying `[RateLimit]` is limited with nothing else written.
A handler with no store registered is not limited: the filter resolves `IRateLimitStore` per
request and passes the request straight through when there is none.

::: danger Each instance counts separately
Two replicas behind a load balancer allow twice the configured limit between them. On Lambda
every execution environment has its own count, and the number of environments is what you do
not control, so there this is not a limit and the work belongs in an API Gateway usage plan or a
WAF. A shared count needs a [store of your own](#where-the-counting-happens).
:::

## Decide what is being limited

`Scope` decides both whose volume is counted and where the filter runs.

`RateLimitScope.Transport` runs at `FilterOrder.RateLimitTransport`, ahead of authentication. It
refuses without reading the request body, which is what makes it useful against a flood. A
limiter meant to blunt credential stuffing cannot wait for the credential to be examined,
because examining it is the work being flooded.

`RateLimitScope.Principal` runs at `FilterOrder.RateLimitPrincipal`, after authentication and
ahead of grant authorization. It counts the authenticated caller's volume. A caller who would
have been refused by authorization anyway still spends a permit.

## Two limits on one handler

`[RateLimit]` is `AllowMultiple`. A burst limit beside an hourly one needs different `Name`
values, or the two share a counter:

```csharp
[Post("/search")]
[RateLimit(PermitLimit = 5, WindowSeconds = 1, Name = "burst")]
[RateLimit(PermitLimit = 500, WindowSeconds = 3600, Name = "hourly")]
public Task<Results> Search(Query query) => _index.Search(query);
```

## Whose allowance a request draws from

`IRateLimitPartitioner` answers that. The default partitions by authenticated subject, falling
back to a configured header and then to one shared anonymous bucket:

```csharp
services.AddSingleton(new RateLimitConfiguration { PartitionHeader = "X-Api-Key" });
```

`RateLimitConfiguration` is registered with `RegistrationType.Try`, so a registration the
application makes is the one that wins.

::: warning Only trust a header your proxy sets
A header the caller can set is a bucket the caller can choose. The proxy in front of the
application has to write it and strip it from the inbound request.
:::

There is no partition by remote address. Behind API Gateway, an ALB or CloudFront the socket peer
is the proxy, and `X-Forwarded-For` is a header the caller can write. Unattributable requests
share one bucket rather than getting one each, because a distinct partition per anonymous caller
is unbounded memory with a rate limiter's name on it.

Replace the partitioner by implementing the interface:

```csharp
[SingletonService(Using = RegistrationType.Replace)]
public class ByTenant : IRateLimitPartitioner {
    public string Partition(IExecutionContext context) =>
        context.Request.Headers.TryGetValue("X-Tenant", out var tenant)
            ? "tenant:" + tenant
            : DefaultRateLimitPartitioner.Anonymous;
}
```

## Where the counting happens

`IRateLimitStore` has one method, `Acquire`, which says whether this request fits in this
allowance. Eviction, clock skew, single-flight refresh and what to do when the backing store is
unreachable are properties of an implementation, not of the contract.

`InProcessRateLimitStore` ships and is registered with `Try`, so replacing it takes no framework
change. It uses the BCL's sliding-window limiter, so a caller cannot spend the whole allowance in
the last instant of one window and the whole of the next in the first instant of the following.

An application that needs one shared count implements the interface against something shared:

```csharp
[SingletonService(Using = RegistrationType.Replace)]
public class RedisRateLimitStore : IRateLimitStore {
    public ValueTask<RateLimitDecision> Acquire(
        string partition, RateLimitPolicy policy, CancellationToken cancellationToken) { ... }
}
```

`RateLimitDecision` carries `RetryAfter`, because a distributed store is the only thing that
knows it and a second round trip to ask would double the cost of the request that was just
refused.

### The partition cap

The in-process store tracks 10,000 partitions by default and fails open at the cap:

```csharp
services.AddSingleton(new RateLimitConfiguration { MaxTrackedPartitions = 50_000 });
```

A partition per caller is unbounded by construction. Refusing at the cap would let anyone who can
mint partition keys deny service to everybody by filling the table. Failing open means the limit
stops being enforced for new partitions, which is the same position as not having deployed a
limiter.

## How a refusal travels

Ahead of `FilterOrder.Serialization`, the filter records the refusal on the response and calls
`Next()` anyway. The filter that turns a failure into bytes sits behind it, so returning early
would produce a 429 with an empty body. The serialization filter finds a request already decided,
reads no body, invokes no handler, and writes the refusal on the way out. Authorization follows
the same rule, and the [response cache](/guide/response-caching#what-is-not-cached) checks for a
recorded failure rather than reading "still travelling" as "still permitted".

## Not built

- **Queueing and concurrency limits.** `[RateLimit]` expresses a permit count over a window and
  nothing else.
- **A distributed store.** The interface is the seam; only the in-process implementation ships.

## Next

- [Request timeouts](/guide/request-timeouts): the other bound on an operation
- [The execution pipeline](/guide/execution-pipeline#the-line-at-serialization): how a refusal ahead of the line travels
- [The OpenAPI document](/guide/openapi-document#what-a-guard-on-the-operation-publishes): the 429 the document carries

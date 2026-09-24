# Rate limiting

`[RateLimit]` limits how many requests each caller can make to a handler in a window of time. A
request over the limit gets 429 Too Many Requests with a `Retry-After` header, and the handler does
not run.

In this excerpt of `src/Todos/TodoController.cs`, the limit on `All` allows each caller three
requests a minute:

```csharp
using Hardened.Requests.Runtime.RateLimiting;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class TodoController
{
    [Operation("listTodos")]
    [Get("/")]
    [RateLimit(PermitLimit = 3, WindowSeconds = 60)]
    public Task<IReadOnlyList<Todo>> All(ITodoStore store) => store.All();
}
```

Every allowed response carries `RateLimit-Limit`, `RateLimit-Remaining` and `RateLimit-Reset`. The
first request gets 200:

```http
GET /todos

HTTP/1.1 200 OK
Content-Type: application/json
RateLimit-Limit: 3
RateLimit-Remaining: 2
RateLimit-Reset: 60

[{"id":1,"title":"Read the generated code","done":true},{"id":2,"title":"Add an endpoint","done":false}]
```

The fourth request within the minute gets 429:

```http
GET /todos

HTTP/1.1 429 Too Many Requests
Content-Type: application/json
Retry-After: 60
RateLimit-Limit: 3
RateLimit-Remaining: 0
RateLimit-Reset: 60

{"type":"RateLimitExceededException","message":"Rate limit exceeded.","details":""}
```

`[RateLimit]` is in the namespace `Hardened.Requests.Runtime.RateLimiting`. Nothing has to be
installed or registered. `InProcessRateLimitStore` is registered by default. It counts in the
application's memory.

## Declaring a limit

`[RateLimit]` takes these properties:

| Property | Default | Meaning |
|---|---|---|
| `PermitLimit` | `100` | Requests allowed in the window |
| `WindowSeconds` | `60` | The window, in seconds |
| `Name` | `"default"` | The count the limit uses. See [What a limit counts](#what-a-limit-counts) |
| `Scope` | `RateLimitScope.Transport` | Where the limit runs. See [Scope](#scope) |

A limit covers different handlers depending on where it is declared:

| Declaration | Covers |
|---|---|
| `[RateLimit]` on a handler method | That handler |
| `[RateLimit]` on a controller class | Every handler in the class. A method's own `[RateLimit]` applies as well |
| `[RateLimit]` on a `[HardenedModule]` class | Every handler compiled in the module's project, except a handler whose method or class has its own `[RateLimit]`. It does not cover the framework's handlers |
| `services.AddGlobalFilter(new RateLimitAttribute { ... })` | Every handler in the application, the health endpoints and `/openapi.json` included. A handler's own `[RateLimit]` applies as well |

`[RateLimit]` can be written more than once on one method or class, as in
[Two limits on one handler](#two-limits-on-one-handler).

A limit on the module goes on the `[HardenedModule]` class in `src/Todos/TodosLibrary.cs`:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.RateLimiting;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization.Metadata;

namespace Todos;

[HardenedModule]
[HardenedWebModule]
[BasePath("/todos")]
[Server("http://localhost:5080", "Local")]
[Enable<OpenApiDocumentPublishing>]
[RateLimit(PermitLimit = 100, WindowSeconds = 60)]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);
    }
}
```

A module's `[RateLimit]` is one count for each caller across every handler it covers.

`AddGlobalFilter` in `ConfigureServices` limits every handler in the application:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.RateLimiting;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization.Metadata;

namespace Todos;

[HardenedModule]
[HardenedWebModule]
[BasePath("/todos")]
[Server("http://localhost:5080", "Local")]
[Enable<OpenApiDocumentPublishing>]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);

        services.AddGlobalFilter(
            new RateLimitAttribute { PermitLimit = 1000, WindowSeconds = 60, Name = "application" }
        );
    }
}
```

`AddGlobalFilter` is an extension method in `Hardened.Requests.Runtime.Filters`. Its optional
`when` argument chooses the handlers. [The execution pipeline](/guide/execution-pipeline) page
covers both.

`[RateLimit]` on a method, a class or a module puts a 429 in the OpenAPI document for each handler
it covers. A limit added with `AddGlobalFilter` puts nothing there.
[The OpenAPI document](/guide/openapi-document) page lists what `[RateLimit]` publishes.

## What a limit counts

A limit counts each caller's requests over a sliding window of `WindowSeconds`. A permit that a
request spends returns one window later. The window moves in steps of one eighth of
`WindowSeconds`.

Requests spend permits as follows:

| Request | Spends |
|---|---|
| A request that the limit allows, whatever its response | A permit. A 400 for a route value or a body that did not bind, a 404, a 409 and a 500 each spend one |
| A HEAD request | A permit from its GET handler's limit |
| A request that reaches no handler, such as one answered 405 | Nothing |
| A request that the limit refuses | Nothing |

A request over the limit is refused at once. No request waits for a permit. `[RateLimit]` does not
limit how many requests run at the same time.

The count is kept for each `Name` and caller, not for each handler. Handlers whose limits have the
same `Name` spend from one count. A count takes its `PermitLimit` and `WindowSeconds` from the
request that created it. A handler under the same `Name` with a different `PermitLimit` is held to
the count's `PermitLimit`. Its `RateLimit-Limit` header still shows its own `PermitLimit`.

::: warning
A `[RateLimit]` without a `Name` is named `"default"`. Two handlers that each carry one share a
count for each caller. The count is held to the limit of the handler that the caller reached first.
Nothing reports it. A limit that should count on its own needs a `Name` that no other limit uses.
:::

The in-process store counts in the memory of one process. Each instance of the application keeps
its own counts. Two instances with `PermitLimit = 3` allow six requests between them. On AWS Lambda
each execution environment is a separate process, so each keeps its own counts. A count shared by
every instance needs a store of the application's own. See [Stores](#stores).

## Headers and the 429 response

The rate limit headers take these values:

| Header | On an allowed response | On a 429 |
|---|---|---|
| `RateLimit-Limit` | The handler's `PermitLimit` | The handler's `PermitLimit` |
| `RateLimit-Remaining` | Permits left in the count | `0` |
| `RateLimit-Reset` | `WindowSeconds` | The same value as `Retry-After` |
| `Retry-After` | Not sent | Seconds to wait, rounded up and at least 1 |

`RateLimit-Reset` on an allowed response is the length of the window. It is not the time until a
permit returns. With the in-process store, `Retry-After` is always `WindowSeconds`, however soon a
permit returns.

The 429's body is
`{"type":"RateLimitExceededException","message":"Rate limit exceeded.","details":""}`. A refused
request's body is not read. A request over the limit with a malformed JSON body gets the 429, not a
400.

When a later filter also refuses the request, that filter's answer is sent in place of the 429. The
answer carries no rate limit headers. An anonymous request over the limit to a handler with
`[Authorize<TScheme>]` gets 401. A request over the limit with `Content-Encoding: deflate` gets 415.
[Authentication](/guide/authentication) covers `[Authorize<TScheme>]`.
[Compression](/guide/compression) covers the 415.

## Scope

`Scope` sets where the limit runs in the filter chain:

| `Scope` | Runs at |
|---|---|
| `RateLimitScope.Transport`, the default | `FilterOrder.RateLimitTransport`, 1000 |
| `RateLimitScope.Principal` | `FilterOrder.RateLimitPrincipal`, 3000 |

[Authentication](/guide/authentication) runs before every filter, so a limit sees the
authenticated caller in either scope. The default partitioner in [Partitions](#partitions) gives
the same partition in both. The application's principal sources run for every request, including a
request that the limit refuses. Both scopes refuse before the request body is read.

No filter that ships with Hardened runs between the two positions. `Scope` decides whether a limit
runs before or after a filter of the application's own that is ordered between them.
[The execution pipeline](/guide/execution-pipeline) page covers filter order.

## Two limits on one handler

A handler can carry a short limit and a long one. Each needs its own `Name`. Two limits with the
same `Name` on one handler spend twice from one count on each request.

In this excerpt of `src/Todos/TodoController.cs`, `Create` allows each caller five requests a
second and 500 an hour:

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Runtime.RateLimiting;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;

namespace Todos;

public class TodoController
{
    [Operation("createTodo")]
    [Post("/")]
    [RateLimit(PermitLimit = 5, WindowSeconds = 1, Name = "burst")]
    [RateLimit(PermitLimit = 500, WindowSeconds = 3600, Name = "hourly")]
    public async Task<Response<Created<Todo>, Conflict>> Create(ITodoStore store, NewTodo request)
    {
        if (await store.TitleExists(request.Title))
        {
            return new Conflict($"A todo titled '{request.Title}' already exists.");
        }

        var todo = await store.Add(request.Title);

        return new Created<Todo>(todo, $"/todos/{todo.Id}");
    }
}
```

A request that one limit refuses still spends a permit from the other. An allowed response's
`RateLimit-*` headers describe one of the limits. With both on the method, it is the one written
last. The first request gets 201:

```http
POST /todos
Content-Type: application/json

{"title":"Todo 1"}

HTTP/1.1 201 Created
Content-Type: application/json
Location: /todos/3
RateLimit-Limit: 500
RateLimit-Remaining: 499
RateLimit-Reset: 3600

{"id":3,"title":"Todo 1","done":false}
```

A 429's headers describe the limit that refused. The sixth request within the same second gets 429
from `burst`:

```http
POST /todos
Content-Type: application/json

{"title":"Todo 6"}

HTTP/1.1 429 Too Many Requests
Content-Type: application/json
Retry-After: 1
RateLimit-Limit: 5
RateLimit-Remaining: 0
RateLimit-Reset: 1

{"type":"RateLimitExceededException","message":"Rate limit exceeded.","details":""}
```

## Partitions

The partition decides which count a request spends from. `DefaultRateLimitPartitioner` chooses it
from the request, in this order:

| The request has | It counts as | The partition a store receives |
|---|---|---|
| An authenticated caller with a subject | That caller | `sub:ada` |
| The header `PartitionHeader` names, with a value | That value | `X-Api-Key:build-server` |
| Neither | One count shared by every such request | `anonymous` |

The default partitioner does not read the client's address. `PartitionHeader` is empty by default,
so every request without an authenticated caller shares the `anonymous` count.

A `RateLimitConfiguration` registered in `ConfigureServices` sets `PartitionHeader`. The framework
registers its own with `RegistrationType.Try`, so the application's is used. The library module in
`src/Todos/TodosLibrary.cs` partitions requests without an authenticated caller by the `X-Api-Key`
header:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.RateLimiting;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization.Metadata;

namespace Todos;

[HardenedModule]
[HardenedWebModule]
[BasePath("/todos")]
[Server("http://localhost:5080", "Local")]
[Enable<OpenApiDocumentPublishing>]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);

        services.AddSingleton(new RateLimitConfiguration { PartitionHeader = "X-Api-Key" });
    }
}
```

The default partitioner uses the header's value as the request sends it. A request that sends a
different value counts for a different partition. The header name is matched without regard to
case. With the limit from the first example on `All`, the fourth request with one key is refused:

```http
GET /todos
X-Api-Key: build-server

HTTP/1.1 429 Too Many Requests
Content-Type: application/json
Retry-After: 60
RateLimit-Limit: 3
RateLimit-Remaining: 0
RateLimit-Reset: 60

{"type":"RateLimitExceededException","message":"Rate limit exceeded.","details":""}
```

The next request, with another key, is allowed:

```http
GET /todos
X-Api-Key: dashboard

HTTP/1.1 200 OK
Content-Type: application/json
RateLimit-Limit: 3
RateLimit-Remaining: 2
RateLimit-Reset: 60

[{"id":1,"title":"Read the generated code","done":true},{"id":2,"title":"Add an endpoint","done":false}]
```

### Replacing the partitioner

A class that implements `IRateLimitPartitioner` and carries
`[SingletonService(Using = RegistrationType.Replace)]` replaces the default partitioner. The
interface has one method, `Partition(IExecutionContext context)`, which returns the partition.
`DefaultRateLimitPartitioner.Anonymous` is `"anonymous"`. [Registering services](/guide/services)
covers `RegistrationType`.

The application's principal source puts a `tenant` claim on the caller. The partitioner in
`src/Todos/TenantPartitioner.cs` reads that claim and counts every caller of one tenant together:

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.RateLimiting;

namespace Todos;

[SingletonService(Using = RegistrationType.Replace)]
public class TenantPartitioner : IRateLimitPartitioner
{
    public string Partition(IExecutionContext context)
    {
        if (context.CallerPrincipal.TryGetClaim("tenant", out var tenant))
        {
            return "tenant:" + tenant;
        }

        return DefaultRateLimitPartitioner.Anonymous;
    }
}
```

[Authentication](/guide/authentication) covers principal sources and claims.

## Stores

`IRateLimitStore` does the counting. Its one method is
`Acquire(string partition, RateLimitPolicy policy, CancellationToken cancellationToken)`, which
returns `ValueTask<RateLimitDecision>`. Each limit calls `Acquire` once for each request. The policy
carries the limit's `PermitLimit`, `Window` and `Name`.

`RateLimitDecision.Allow(limit, remaining)` lets the request through.
`RateLimitDecision.Refuse(limit, retryAfter)` refuses it. The decision's values become the headers
in [Headers and the 429 response](#headers-and-the-429-response).

A store that carries `[SingletonService(Using = RegistrationType.Replace)]` replaces the in-process
store. When `Acquire` throws, the request gets 500 with an empty body. The request is logged as
failed.

The next example keeps a fixed-window count in Redis through `StackExchange.Redis`, so every
instance of the application shares it. It needs Redis 7.0 or later, for `ExpireWhen.HasNoExpiry`.
In the solution directory, this command adds the package to `src/Todos`:

```bash
dotnet add src/Todos package StackExchange.Redis
```

`src/Todos/RedisRateLimitStore.cs` holds the store:

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Runtime.RateLimiting;
using StackExchange.Redis;

namespace Todos;

[SingletonService(Using = RegistrationType.Replace)]
public class RedisRateLimitStore(IConnectionMultiplexer redis) : IRateLimitStore
{
    public async ValueTask<RateLimitDecision> Acquire(
        string partition,
        RateLimitPolicy policy,
        CancellationToken cancellationToken
    )
    {
        var database = redis.GetDatabase();
        var key = $"rate-limit:{policy.Name}:{partition}";

        var count = await database.StringIncrementAsync(key);

        await database.KeyExpireAsync(key, policy.Window, ExpireWhen.HasNoExpiry);

        if (count <= policy.PermitLimit)
        {
            return RateLimitDecision.Allow(policy.PermitLimit, policy.PermitLimit - (int)count);
        }

        var retryAfter = await database.KeyTimeToLiveAsync(key) ?? policy.Window;

        return RateLimitDecision.Refuse(policy.PermitLimit, retryAfter);
    }
}
```

The library module in `src/Todos/TodosLibrary.cs` registers the Redis connection:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using System.Text.Json.Serialization.Metadata;

namespace Todos;

[HardenedModule]
[HardenedWebModule]
[BasePath("/todos")]
[Server("http://localhost:5080", "Local")]
[Enable<OpenApiDocumentPublishing>]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);

        services.AddSingleton<IConnectionMultiplexer>(
            _ => ConnectionMultiplexer.Connect("localhost:6379")
        );
    }
}
```

With the limit from the first example on `All`, three requests go to two instances. A fourth
request, sent to the second instance, is refused:

```http
GET /todos

HTTP/1.1 429 Too Many Requests
Content-Type: application/json
Retry-After: 60
RateLimit-Limit: 3
RateLimit-Remaining: 0
RateLimit-Reset: 60

{"type":"RateLimitExceededException","message":"Rate limit exceeded.","details":""}
```

### How many counts the in-process store keeps

The in-process store keeps at most `MaxTrackedPartitions` counts, 10,000 by default. Each pair of
`Name` and partition is one count. Once the store keeps that many, a request for a new pair is let
through without being counted. Its response has `RateLimit-Remaining` equal to `PermitLimit`. The
pairs the store already keeps are still limited.

The store removes no count while the process runs. A partition that first arrives once the store is
full is not limited until the process restarts.

Set these on the rate limit configuration, registered as in [Partitions](#partitions):

| Property | Default | Meaning |
|---|---|---|
| `PartitionHeader` | `""` | The header that partitions requests without an authenticated caller |
| `MaxTrackedPartitions` | `10_000` | The most counts the in-process store keeps |

## Next

| Page | Covers |
|---|---|
| [The OpenAPI document](/guide/openapi-document) | The 429 that `[RateLimit]` publishes |
| [Authentication](/guide/authentication) | Principal sources, which set the caller a partition comes from |
| [The execution pipeline](/guide/execution-pipeline) | Filter order, and `AddGlobalFilter` |
| [Request timeouts](/guide/request-timeouts) | Bounding how long a handler runs |
| [Declared responses](/guide/responses) | `RateLimited`, a 429 that a handler returns itself |

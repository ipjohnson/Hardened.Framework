# Response caching

`[CacheResponse<T>]` on a handler stores the handler's response. A later request with the same key gets the stored response, and the handler does not run.

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Runtime.Caching;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Caching;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

public class TodoController
{
    [Operation("getTodo")]
    [Get("/{id}")]
    [CacheResponse<VaryByRoute>(Duration = 60)]
    public async Task<Response<Todo, NotFound>> ById(ITodoStore store, [Range(Min = 1)] int id)
    {
        var todo = await store.Find(id);

        if (todo is null)
        {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return todo;
    }
}
```

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json
ETag: "+x0430/JrxmEstC33HEuf0Yg/61Cvwmwtx2WMTybq/s="

{"id":1,"title":"Read the generated code","done":true}
```

```http
DELETE /todos/1

HTTP/1.1 204 No Content
```

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json
ETag: "+x0430/JrxmEstC33HEuf0Yg/61Cvwmwtx2WMTybq/s="

{"id":1,"title":"Read the generated code","done":true}
```

After `DELETE /todos/1` answered 204, `GET /todos/1` still answered 200 with the stored body. The cache computed the `ETag` on both 200 responses.

The type argument names what the entry is keyed on. `VaryByRoute` keys it on the route's tokens, so `GET /todos/1` and `GET /todos/2` are two entries.

The attribute needs a store. A separate package supplies it.

## Adding a store

Nothing registers a store by default. The `Hardened.Requests.Caching.Memory` package supplies one that keeps its entries in the process's memory.

The `hardened-web` template's `Directory.Packages.props` already lists the package, so the library project needs only this reference in `src/Todos/Todos.csproj`:

```xml
<PackageReference Include="Hardened.Requests.Caching.Memory" />
```

`dotnet add package` with `--version` rewrites the package's `PackageVersion` in `Directory.Packages.props` to a literal version. It also removes the file's blank lines. Without `--version`, the command fails because no stable version exists.

A project that does not use central package management gives the version:

```xml
<PackageReference Include="Hardened.Requests.Caching.Memory" Version="0.0.0-HARDENED-VERSION" />
```

`[HardenedMemoryResponseCache]`, in the namespace `Hardened.Requests.Caching.Memory`, registers the store. A module that applies it registers the store for every application that imports the module. Put it on the library module in `src/Todos/TodosLibrary.cs`, beside the handlers:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Caching.Memory;
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
[HardenedMemoryResponseCache]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);
    }
}
```

The template's tests build the library module alone. `tests/Todos.Tests/Bootstrap.cs` declares `[assembly: HardenedTestEntryPoint(typeof(TodosLibrary))]`. With the store on the host's `Application` instead, the application answers normally. The build reports nothing. Every test that calls a cached handler gets 500.

### Without a store

Without a store, the build warns with `HRDW005` on the project whose module applies `[KestrelRuntime]` or `[AspNetCoreRuntime]`. One warning covers the project. It names every cached handler, including the handlers in imported modules. For the `ById` handler above, the build reports it on `Todos.Host.csproj`:

```text
CSC : warning HRDW005: 'TodoController.ById' declares [CacheResponse] and this application registers no response cache store, so every request to it answers an error. Add the Hardened.Requests.Caching.Memory package and [HardenedMemoryResponseCache] to the module, or register an IResponseCacheStore yourself and suppress HRDW005.
```

On the Lambda, Cloud Run and Azure Functions hosts, the build reports nothing.

The application starts normally. Every request to a cached handler answers 500 with the error body:

```http
GET /todos/1

HTTP/1.1 500 Internal Server Error
Content-Type: application/json

{"type":"ServerError","message":"The server could not complete this request.","details":""}
```

A request that the handler would answer 404 gets the same 500. The log names the handler by its route template:

```text
Hardened.Requests.Runtime.Caching.ResponseCacheStoreMissingException: GET /todos/{id} declares [CacheResponse] and no IResponseCacheStore is registered. Reference Hardened.Requests.Caching.Memory and add [HardenedMemoryResponseCache] to the application module, or register a store of your own.
```

The message names the application module. In a project whose tests build the library module, as the template's do, the store goes on the library module.

## The attribute

`CacheResponseAttribute<TProvider>` is in the namespace `Hardened.Requests.Runtime.Caching`, which the `Hardened.Web.Runtime` package brings. It goes on a method or a class. More than one can go on the same method.

| Member | Type | Default | Meaning |
|---|---|---|---|
| `TProvider` | Type argument | None | The key strategy |
| Positional values | `params string[]` | Empty | The strategy's arguments, such as query keys |
| `Duration` | `int` | `0`, which means 60 | Seconds the store keeps the entry |
| `Scope` | `CacheScope` | `CacheScope.Unstated` | Who a stored response may be served to |
| `Tags` | `string[]` | Empty | Names that evict the entry |

## Keys and strategies

The key starts with the handler's method and route template, such as `GET /todos/{id}`, so two handlers never share an entry. The rest of the key follows in this order:

| Key part | Included when |
|---|---|
| The caller | The scope is `PerCaller` |
| The representation | The operation produces more than one media type |
| Each strategy's part, in the order the attributes are written | Always |

Hardened supplies these strategies:

| Strategy | Namespace | Arguments | Keys on |
|---|---|---|---|
| `VaryByRoute` | `Hardened.Web.Runtime.Caching` | None | Every token in the route template |
| `VaryByQuery` | `Hardened.Web.Runtime.Caching` | One or more query keys | The named query values |
| `VaryByHeader` | `Hardened.Web.Runtime.Caching` | One or more header names | The named request headers |
| `ByPayload` | `Hardened.Requests.Runtime.Caching` | None | The SHA-256 of the request body |

`VaryByRoute` reads every token that the route declares, including a token that the handler does not bind. A route with no token has one entry.

`VaryByQuery` reads only the keys it names. A request with another query parameter gets the same entry. An absent key and an empty value are the same entry.

`VaryByHeader` adds each header it names to the response's `Vary` header. It matches the header name in any case. A request without the header is one more entry. [Compression](/guide/compression) covers how this `Vary` header is merged with the one that compression writes.

`ByPayload` hashes the raw body bytes. A body with other whitespace is another entry. The handler still binds the body.

This handler is keyed on its `title` query value:

```csharp
using Hardened.Requests.Runtime.Caching;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Caching;

namespace Todos;

public class SearchController
{
    [Get("/search")]
    [CacheResponse<VaryByQuery>("title", Duration = 60)]
    public async Task<IReadOnlyList<Todo>> Search(ITodoStore store, [FromQueryString] string title)
    {
        var todos = await store.All();

        return todos
            .Where(todo => todo.Title.Contains(title, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
```

```http
GET /todos/search?title=read

HTTP/1.1 200 OK
Content-Type: application/json
ETag: "a18rjkw5d/HSOBfWB1EbmO3a6Lk7y+7ZYDu7bHwhbRc="

[{"id":1,"title":"Read the generated code","done":true}]
```

`GET /todos/search?title=read&page=2` is then answered from the same entry, with the same `ETag`.

### Arguments a strategy refuses

A strategy that is given arguments it cannot use fails when the handler's chain is built. The chain is built on the first request to the route. That request and every later one answer 500 with the error body. The log names the handler and gives the strategy's message. With `[CacheResponse<VaryByRoute>("title", Duration = 60)]` in place of the `VaryByQuery` declaration on `SearchController`, the log shows:

```text
System.InvalidOperationException: [CacheResponse] on GET /todos/search could not build its cache key strategy: VaryByRoute keys on the route's own tokens and takes no values, but was given title. Name query keys with VaryByQuery instead. (Parameter 'values')
```

The strategies give these messages:

| Declaration | Message |
|---|---|
| `[CacheResponse<VaryByRoute>("title")]` | `VaryByRoute keys on the route's own tokens and takes no values, but was given title. Name query keys with VaryByQuery instead. (Parameter 'values')` |
| `[CacheResponse<VaryByQuery>]` | `VaryByQuery needs at least one query key to vary on. (Parameter 'values')` |
| `[CacheResponse<VaryByHeader>("Cookie")]` | `VaryByHeader will not vary on Cookie. A response keyed on a session has one caller, so the entry is never hit. Write a key provider over the claim that actually varies the answer. (Parameter 'values')` |
| `[CacheResponse<VaryByHeader>]` | `VaryByHeader needs at least one header name to vary on. (Parameter 'values')` |
| `[CacheResponse<ByPayload>("title")]` | `ByPayload keys on the request body and takes no values, but was given title. (Parameter 'values')` |

### Writing a strategy

A strategy implements `ICacheKeyProvider`, in `Hardened.Requests.Abstract.Caching`. `static Create(string[] values)` builds it once for each handler, from the attribute's positional arguments. `Create` can throw for arguments it cannot use. The handler then fails as the table above shows. `Key(IExecutionContext context)` returns the request's part of the key. A null key leaves the request uncached. The store is neither read nor written for it.

This strategy keys on the caller's `tenant` claim:

```csharp
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;

namespace Todos;

public sealed class VaryByTenant : ICacheKeyProvider
{
    public static ICacheKeyProvider Create(string[] values) => new VaryByTenant();

    public ValueTask<string?> Key(IExecutionContext context) =>
        new(context.CallerPrincipal.TryGetClaim("tenant", out var tenant) ? tenant : null);
}
```

`[CacheResponse<VaryByTenant>(Duration = 60)]` on a handler gives the callers of each tenant their own entry. A request from a caller with no `tenant` claim, or from an anonymous caller, is not cached. [Authentication](/guide/authentication) covers claims.

## More than one declaration

### Two strategies on one handler

Two declarations on one method make one `ResponseCacheFilter` with one key. A request that differs in either part is another entry. A null part from any one strategy leaves the whole request uncached.

The first `Duration` that is set applies. Two different durations fail when the chain is built, the same way as a refused argument. Two different scopes fail the same way. Tags from every declaration form one set.

`SearchController` with `[CacheResponse<VaryByQuery>("title", Duration = 60)]` and `[CacheResponse<VaryByHeader>("Accept-Language", Duration = 300)]` logs:

```text
System.InvalidOperationException: GET /todos/search declares [CacheResponse] twice with different durations, 60 and 300. Composed attributes share one lifetime, so set Duration on one of them.
```

### On a class, a module or every handler

`[CacheResponse<T>]` also goes on a controller class or a `[HardenedModule]` class. `services.AddGlobalFilter(attribute, when)`, in `Hardened.Requests.Runtime.Filters`, applies one to every handler that `when` accepts.

| Declaration | Covers | A handler's own declaration |
|---|---|---|
| On a method | That handler | Not applicable |
| On a controller class | Every handler in the class, any method | Composes with it |
| On a `[HardenedModule]` class | Every handler compiled in that project, any method | Replaces it when the closed type is the same, and composes with it otherwise |
| `AddGlobalFilter` | Every handler `when` accepts, the framework's included | Replaces it |

A module declaration does not cover the framework's handlers, such as `/health/live` and `/openapi.json`. Under a module's `[CacheResponse<VaryByRoute>]`, a handler with its own `[CacheResponse<VaryByRoute>]` on its method or class uses only its own. A handler with `[CacheResponse<VaryByQuery>("x")]` gets both, composed into one key. Two different durations then fail.

The filter caches a handler for any method.

::: warning
On a class or a module, `[CacheResponse<T>]` also covers the POST, PUT, PATCH and DELETE handlers. It stores a 200 from any of them. A later write to the same key gets that stored response, and the handler does not run. Declare the attribute on the GET methods, or register it with `AddGlobalFilter` and a `when` that admits only GET.
:::

Under `AddGlobalFilter`, a handler that declares any `[CacheResponse<T>]` of its own uses only its own. `AddGlobalFilter` also covers the framework's GET handlers. With this registration, `/health/live`, `/health/ready`, `/openapi.json` and `/docs` are stored:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Caching.Memory;
using Hardened.Requests.Runtime.Caching;
using Hardened.Requests.Runtime.Filters;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Caching;
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
[HardenedMemoryResponseCache]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);

        services.AddGlobalFilter(
            new CacheResponseAttribute<VaryByRoute> { Duration = 60 },
            when: handler => handler.Method == "GET"
        );
    }
}
```

[The execution pipeline](/guide/execution-pipeline) covers `AddGlobalFilter` for every filter.

## More than one media type

An operation that produces more than one media type gets one entry for each representation. The media types come from `[Produces]` on the method or class, `[assembly: Produces]`, or a registered `ResponseContentTypeDefault`. Here `ById` produces JSON and CSV:

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Runtime.Caching;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Caching;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

public class TodoController
{
    [Operation("getTodo")]
    [Get("/{id}")]
    [Produces("application/json", "text/csv")]
    [CacheResponse<VaryByRoute>(Duration = 60)]
    public async Task<Response<Todo, NotFound>> ById(ITodoStore store, [Range(Min = 1)] int id)
    {
        var todo = await store.Find(id);

        if (todo is null)
        {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return todo;
    }
}
```

`CsvResponseSerializer` is the example serializer on [Content negotiation](/guide/content-negotiation). That page covers `[Produces]`. With the serializer in `src/Todos`, a JSON caller and a CSV caller each get their own stored response:

```http
GET /todos/1
Accept: application/json

HTTP/1.1 200 OK
Content-Type: application/json
ETag: "+x0430/JrxmEstC33HEuf0Yg/61Cvwmwtx2WMTybq/s="
Vary: Accept

{"id":1,"title":"Read the generated code","done":true}
```

```http
GET /todos/1
Accept: text/csv

HTTP/1.1 200 OK
Content-Type: text/csv
ETag: "XWztuaFFm6FIsWYSodVyE4TtfDHTfxtxlzCHNdnuoQA="
Vary: Accept

id,title,done
1,Read the generated code,True
```

Every response from such an operation carries `Vary: Accept`, including a hit, a 404 and a 406.

The key holds the representation that the request negotiates to, not the `Accept` header. A browser's `Accept` header that negotiates to JSON gets the JSON entry. A request for a media type that the operation does not produce is answered 406. A 406 is not stored.

An operation with a single media type, from any of those sources, has one entry and no `Vary: Accept`.

## Who a stored response is served to

`CacheScope`, in `Hardened.Requests.Abstract.Caching`, says who a stored response may be served to.

| Value | Meaning |
|---|---|
| `Unstated` | The default. `AllCallers` on a handler with no authorization requirement, and a failure on a handler with one |
| `AllCallers` | One entry, served to every caller the handler's authorization admits |
| `PerCaller` | One entry for each caller. The caller's issuer and subject are part of the key |

A handler with no authorization requirement needs no scope. A handler with an authorization requirement must set `Scope`:

```csharp
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.Caching;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Caching;

namespace Todos;

public class ProfileController
{
    [Get("/profile")]
    [AuthorizeGrants("todos:read")]
    [CacheResponse<VaryByRoute>(Duration = 60, Scope = CacheScope.PerCaller)]
    public string Profile(IExecutionContext context) =>
        $"Signed in as {context.CallerPrincipal.Subject}";
}
```

The example runs with a principal source that accepts two bearer tokens with the grant `todos:read`. The token `reader` has the subject `ria`. The token `pia` has the subject `pia`.

```http
GET /todos/profile
Authorization: Bearer reader

HTTP/1.1 200 OK
Content-Type: application/json
ETag: "mgP1Qv973lbMK1JIKEJAiiZdv2PK4xfaxuxlhKNF+Tk="

"Signed in as ria"
```

```http
GET /todos/profile
Authorization: Bearer pia

HTTP/1.1 200 OK
Content-Type: application/json
ETag: "/xmQdU9vYVGiSu446QP+8zQVx5ep9TZ4W3r5SaMPimk="

"Signed in as pia"
```

Without `Scope`, the handler's chain fails when it is built. Every request to the route answers 500, including a request that the requirement would refuse. The log names the handler and its requirement:

```text
Hardened.Requests.Runtime.Caching.CacheScopeUndeclaredException: GET /todos/profile requires todos:read of its caller and declares [CacheResponse] without saying who a stored response may be served to. Set Scope = CacheScope.PerCaller if the answer depends on who asked - an owner-scoped read, anything filtered by the caller's tenant - or Scope = CacheScope.AllCallers if every caller the guard admits gets the same bytes.
```

`CacheScopeUndeclaredException`, in `Hardened.Requests.Runtime.Caching`, carries the handler as `Handler`, such as `GET /todos/profile`.

Authorization still runs on every request. A caller that it refuses gets the refusal, not a stored response.

Under `AllCallers`, every admitted caller gets the response that the first caller filled, whatever the handler reads from the caller.

Under `PerCaller`, two issuers that name the same subject are two callers. A request from a caller with no subject is neither answered from the store nor stored.

[Authorization](/guide/authorization) covers requirements. [Authentication](/guide/authentication) covers callers.

## What is not cached

A handler whose requirement contains `Requirement.Predicate`, which reads the request, is never cached. `[CacheResponse<T>]` installs no filter on it, whatever its scope. A handler that requires only grants, or only an authenticated caller, is cached.

Only a 200 is stored. A 201, a 204, a 400, a 404 and a 500 are sent and not stored.

A request is neither answered from the store nor stored when a strategy returns null for it, or when the scope is `PerCaller` and the caller has no subject.

A handler that returns `IAsyncEnumerable<T>` is stored like any other. The cache holds the whole sequence and sends it after the last item.

## Duration and tags

`Duration` is whole seconds. `0`, the default, means 60. The entry expires a fixed time after it was stored. A hit does not extend it.

`Tags` names the entry. A handler takes `IResponseCacheStore`, in `Hardened.Requests.Abstract.Caching`, as a parameter from the container. Its `EvictByTag(tag, cancellationToken)` drops every entry stored under that tag:

```csharp
using Hardened.Requests.Abstract.Caching;
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Runtime.Caching;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Caching;
using Hardened.Web.Runtime.Responses;

namespace Todos;

public class TodoController
{
    [Operation("listTodos")]
    [Get("/")]
    [CacheResponse<VaryByRoute>(Duration = 3600, Tags = ["todos"])]
    public Task<IReadOnlyList<Todo>> All(ITodoStore store) => store.All();

    [Operation("createTodo")]
    [Post("/")]
    public async Task<Response<Created<Todo>, Conflict>> Create(
        ITodoStore store,
        IResponseCacheStore cache,
        NewTodo request,
        CancellationToken cancellationToken
    )
    {
        if (await store.TitleExists(request.Title))
        {
            return new Conflict($"A todo titled '{request.Title}' already exists.");
        }

        var todo = await store.Add(request.Title);

        await cache.EvictByTag("todos", cancellationToken);

        return new Created<Todo>(todo, $"/todos/{todo.Id}");
    }
}
```

```http
GET /todos

HTTP/1.1 200 OK
Content-Type: application/json
ETag: "q3R4DPG22kwIZKmmP/pd3JHDU/YXIa6FbZ574u7p1Xs="

[{"id":1,"title":"Read the generated code","done":true},{"id":2,"title":"Add an endpoint","done":false}]
```

```http
POST /todos
Content-Type: application/json

{"title":"Write the docs"}

HTTP/1.1 201 Created
Content-Type: application/json
Location: /todos/3

{"id":3,"title":"Write the docs","done":false}
```

```http
GET /todos

HTTP/1.1 200 OK
Content-Type: application/json
ETag: "3XmST7QXlJ/M9wX2T4HDwwGEX+xlLc7Rguxy/3vYrNQ="

[{"id":1,"title":"Read the generated code","done":true},{"id":2,"title":"Add an endpoint","done":false},{"id":3,"title":"Write the docs","done":false}]
```

The second `GET /todos` lists the new todo, with a new `ETag`.

`EvictByTag` on a tag that nothing was stored under is not an error. A write that does not evict leaves the stored response in place.

## The stored response

`CachedResponse`, in `Hardened.Requests.Abstract.Caching`, holds the status, the content type, the body bytes, the response headers and the tags.

A hit sends the stored status, headers and body. No header marks a response as a hit. The handler, the binding and the serializer do not run on a hit. Filters ordered ahead of the cache still run. [The execution pipeline](/guide/execution-pipeline) gives the order.

A header that the response already carried when the request reached the cache is not stored. The filter that wrote it writes it again on the next request, so a hit carries the current request's `X-Correlation-Id`.

These headers are never stored, whatever wrote them:

| Headers never stored |
|---|
| `Set-Cookie` |
| `Content-Encoding` |
| `Content-Length`, `Transfer-Encoding`, `Connection`, `Keep-Alive`, `TE`, `Trailer`, `Upgrade`, `Proxy-Authenticate`, `Proxy-Authorization` |
| `Date`, `Server` |

Every other header that the handler and the filters behind the cache wrote is stored and sent on a hit, `Cache-Control` included.

The cache puts an `ETag` on a stored response that has none. The tag is the SHA-256 of the body, base64, in quotes. A handler's own `ETag` is kept. A response that is not stored gets no `ETag` from the cache. [Conditional requests](/guide/conditional-requests) covers answering 304 against that tag.

A HEAD request uses the GET handler's entry. A HEAD that misses fills the entry. A later GET gets the full body from it.

### With compression

The store holds the body before compression. A hit is compressed for a caller that sends `Accept-Encoding: gzip`. It is sent as stored to a caller that does not.

On a compressed response, compression makes the cache's `ETag` weak: `W/"..."`. An entry filled by a compressed response keeps the weak `ETag` and `Vary: Accept-Encoding`. Every hit on that entry sends both, compressed or not. An entry filled by an uncompressed response keeps the strong tag.

[Compression](/guide/compression) covers the rest.

## The `Cache-Control` header

`[CacheControl]` writes the `Cache-Control` response header. It stores nothing. `CacheControlAttribute` is in `Hardened.Web.Runtime.Attributes`. A handler without the attribute sends no `Cache-Control` header.

| Property | Type | Default | Meaning |
|---|---|---|---|
| `MaxAge` | `int` | `0` | Seconds for `max-age` |
| `Type` | `CacheControlEnum` | `CacheControlEnum.MaxAge \| CacheControlEnum.Public` | The directives to write |

With `[CacheResponse<T>]` on the same handler, the stored response keeps the header. A hit sends it too. This handler declares both:

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Runtime.Caching;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Caching;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

public class TodoController
{
    [Operation("getTodo")]
    [Get("/{id}")]
    [CacheResponse<VaryByRoute>(Duration = 60)]
    [CacheControl(MaxAge = 60)]
    public async Task<Response<Todo, NotFound>> ById(ITodoStore store, [Range(Min = 1)] int id)
    {
        var todo = await store.Find(id);

        if (todo is null)
        {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return todo;
    }
}
```

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json
Cache-Control: public, max-age=60
ETag: "+x0430/JrxmEstC33HEuf0Yg/61Cvwmwtx2WMTybq/s="

{"id":1,"title":"Read the generated code","done":true}
```

```http
GET /todos/9

HTTP/1.1 404 Not Found
Content-Type: application/json
Cache-Control: public, max-age=60

{"resource":"todo","detail":"No todo has id 9.","type":"urn:hardened:problem:not-found","title":"Not Found","status":404}
```

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json
Cache-Control: public, max-age=60
ETag: "+x0430/JrxmEstC33HEuf0Yg/61Cvwmwtx2WMTybq/s="

{"id":1,"title":"Read the generated code","done":true}
```

The third exchange is a hit. The header is sent on every response to the handler's route, including a 404, a validation 400, a 401 and a 403.

`CacheControlEnum` is in `Hardened.Web.Runtime.CacheControl`. It is a flags enum with the values `MaxAge`, `NoCache`, `NoStore`, `NoTransform`, `Public` and `Private`.

The header lists the directives in this order: `private` or `public`, `no-cache`, `no-store`, `max-age`, `no-transform`. With both `Private` and `Public` set, the header has only `private`. `max-age` is written only when `Type` includes `MaxAge`. Every flag that is set is written, including combinations that an HTTP cache reads as contradictory. A `Type` with no flag writes no header. These declarations send these headers:

| Declaration | Header |
|---|---|
| `[CacheControl]` | `public, max-age=0` |
| `[CacheControl(MaxAge = 60)]` | `public, max-age=60` |
| `[CacheControl(Type = CacheControlEnum.NoStore)]` | `no-store` |
| `[CacheControl(Type = CacheControlEnum.NoCache)]` | `no-cache` |
| `[CacheControl(MaxAge = 60, Type = CacheControlEnum.MaxAge \| CacheControlEnum.Private)]` | `private, max-age=60` |
| `[CacheControl(MaxAge = 60, Type = CacheControlEnum.MaxAge \| CacheControlEnum.Public \| CacheControlEnum.Private)]` | `private, max-age=60` |
| `[CacheControl(MaxAge = 60, Type = CacheControlEnum.Public)]` | `public` |
| `[CacheControl(Type = 0)]` | No header |
| `[CacheControl(MaxAge = 5, Type = ...)]` with all six flags | `private, no-cache, no-store, max-age=5, no-transform` |

On a controller class, `[CacheControl]` covers every handler in the class, a POST handler included. On a `[HardenedModule]` class, it covers every handler compiled in that project. It does not cover the framework's handlers. With `[CacheControl]` on both a class and one of its methods, the method's handler sends the class's header.

## The in-memory store

`[HardenedMemoryResponseCache]` registers `MemoryResponseCacheStore` as the `IResponseCacheStore`. The store keeps its own `MemoryCache`. It does not use an `IMemoryCache` that the application registers.

| Setting | Default | Meaning |
|---|---|---|
| `SizeLimit` | 104857600 bytes (100 MB) | The most the store holds |
| `MaximumBodySize` | 67108864 bytes (64 MB) | The largest body it stores |

An entry counts against `SizeLimit` with its body, its content type, headers and tags at two bytes a character, the key it is stored under, and 512 bytes for the objects around them. `MaximumBodySize` counts the body alone. A response whose body is larger than `MaximumBodySize` is sent and not stored. An entry that `SizeLimit` has no room for is not stored either.

A key carries the values that its strategies read. Under `VaryByQuery` or `VaryByHeader`, a caller who sends a new value on each request adds an entry on each. The limit bounds the memory those entries take. It does not stop them taking the room that other entries would use.

`services.ConfigureMemoryResponseCache(...)`, in `Hardened.Requests.Caching.Memory`, sets the limits:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Caching.Memory;
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
[HardenedMemoryResponseCache]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);

        services.ConfigureMemoryResponseCache(cache =>
        {
            cache.SizeLimit = 32 * 1024 * 1024;
            cache.MaximumBodySize = 1024 * 1024;
        });
    }
}
```

Each call amends the configuration, so two calls both apply. No environment variable sets the limits.

The store reads the time from the `TimeProvider` in the container. `[HardenedMemoryResponseCache]` registers `TimeProvider.System` only when the application has registered none.

An application can register its own `IResponseCacheStore` instead. The application's store is used in place of the in-memory store. The build still reports `HRDW005`, so suppress it. The interface has three members: `Get`, `Set` and `EvictByTag`. A `Set` may store nothing, and the next request then misses.

### In a test

A test that expects a hit sends its requests through an `ITestWebApp` marked `[Shared]`. Without `[Shared]`, each request gets its own container, and the store with it. [Writing a test](/guide/testing) covers `[Shared]` and the container per request.

## Functions and AWS Lambda

`[CacheResponse<T>]` works on a function handler. `ByPayload` keys it on the whole invocation payload. This example is `src/Pricing/QuoteHandler.cs` in a `hardened-function` scaffold named `Pricing`:

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Caching;

namespace Pricing;

public record QuoteRequest(string Sku, int Quantity);

public record Quote(string Sku, int Quantity, decimal Total);

public class QuoteHandler
{
    [HardenedFunction]
    [CacheResponse<ByPayload>(Duration = 300)]
    public Quote Price(QuoteRequest request) =>
        new(request.Sku, request.Quantity, request.Quantity * 4.5m);
}
```

The `hardened-function` template's `Directory.Packages.props` lists `Hardened.Requests.Caching.Memory` too. Add the same `PackageReference` to the function's project, such as `src/Pricing/Pricing.csproj`. The application module registers the store:

```csharp
using Hardened.Requests.Caching.Memory;
using Hardened.Shared.Runtime.Attributes;

namespace Pricing;

[HardenedModule]
[HardenedMemoryResponseCache]
public partial class Application;
```

The in-memory store holds the entries of one process. On Lambda, each execution environment has its own entries. `EvictByTag` reaches only the environment that it runs in. A hit still invokes the function. An entry that expired while the execution environment was frozen is not served.

[Triggers](/guide/triggers) covers function handlers.

## Limits

Two requests that miss at the same time both run the handler. The one that finishes last leaves its response in the store.

The cache does not read the request's `Cache-Control` or `Pragma` header. A request with `Cache-Control: no-cache`, `no-store` or `max-age=0` gets the stored response.

The cache does not read the response's own `Cache-Control` either. It stores the response of a handler with `[CacheControl(Type = CacheControlEnum.NoStore)]` and `[CacheResponse<T>]`.

Nothing removes a single entry by its key. Tags are the only way to evict.

## Next

- [Conditional requests](/guide/conditional-requests): answering 304 against the stored `ETag`
- [Compression](/guide/compression): compressing stored responses, and the `Vary` header
- [The execution pipeline](/guide/execution-pipeline): where the cache runs among the other filters
- [Content negotiation](/guide/content-negotiation): declaring the media types an operation produces
- [Diagnostics](/reference/diagnostics): the `HRDW005` warning

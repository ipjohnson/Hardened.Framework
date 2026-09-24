# Conditional requests

`[ConditionalGet]` on a GET handler makes the response carry an `ETag` header. A later request that sends that tag in `If-None-Match` gets `304 Not Modified` and no body.

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Conditional;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

public class TodoController
{
    [Operation("getTodo")]
    [Get("/{id}")]
    [ConditionalGet]
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
GET /todos/1
If-None-Match: "+x0430/JrxmEstC33HEuf0Yg/61Cvwmwtx2WMTybq/s="

HTTP/1.1 304 Not Modified
ETag: "+x0430/JrxmEstC33HEuf0Yg/61Cvwmwtx2WMTybq/s="
```

The example adds the attribute and its `using` line to the `ById` handler of a project made with `dotnet new hardened-web -n Todos`. The attribute is in the namespace `Hardened.Web.Runtime.Conditional`, from the `Hardened.Web.Runtime` package. The template's `src/Todos` project compiles the example with no package added.

## Where to declare it

Each declaration installs `ConditionalGetFilter` on the GET handlers it covers:

| Declaration | Covers | In the OpenAPI document |
|---|---|---|
| `[ConditionalGet]` on a method | That GET handler | Yes |
| `[ConditionalGet]` on a controller class | Every GET handler in the class | Yes |
| `[ConditionalGet]` on a `[HardenedModule]` class | Every GET handler compiled in the module's project | Yes |
| `[Enable<ConditionalGet>]` on the application module | Every GET handler in the application, the framework's included | No |

Every declaration covers GET handlers only. A POST, PUT, PATCH or DELETE handler in a class with `[ConditionalGet]` gets nothing. A HEAD request is routed to the GET handler. It gets the same `ETag`, and a 304 when the tag matches. Every declaration leaves out a handler that returns `IAsyncEnumerable<T>`. That handler gets no `ETag`, no 304 and nothing in the OpenAPI document.

`[ConditionalGet]` on a module class leaves out the framework's handlers and the handlers in other projects, including the host project that imports the module. On the library module in `src/Todos/TodosLibrary.cs`, it covers the GET handlers in `src/Todos`:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Conditional;
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
[ConditionalGet]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);
    }
}
```

`[Enable<ConditionalGet>]` on the application module covers handlers in referenced projects, handlers in the host project, and the framework's `/health/live`, `/openapi.json` and `/docs`. The `ConditionalGet` type is in the same namespace as the attribute. The application module in `src/Todos.Host/Application.cs` enables it for the whole application:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Runtime.Conditional;
using Hardened.Web.Runtime.OpenApi;

namespace Todos.Host;

[HardenedModule]
[KestrelRuntime]
[Enable<ConditionalGet>]
[HardenedOpenApiUi(Title = "Todos", Environments = "development")]
[TodosLibrary]
public partial class Application;
```

The three `[ConditionalGet]` declarations add a 304 response to each GET operation they cover in the OpenAPI document. They also declare the `ETag` header on the 200 and the 304, and the optional `If-None-Match` and `If-Modified-Since` request headers. `[Enable<ConditionalGet>]` is applied at startup, so the document does not show it.

A handler with `[ConditionalGet]` on its method or class keeps only its own declaration. `[Enable<ConditionalGet>]` and `[ConditionalGet]` on the module skip it. `[ConditionalGet]` on both a method and its class installs two filters. The second filter finds the first in place and passes the request through. The handler behaves as it does with one filter.

## How the tag is computed

When the response has no `ETag` at its first write, the filter holds the whole body in memory. When the handler finishes, the filter sets `ETag` to the SHA-256 hash of the body bytes, base64-encoded and quoted. The tag is strong. The same bytes always get the same tag.

When the response already has an `ETag` at its first write, the filter does not hold the body. It sends the body or answers 304 at once.

The handler runs on every request, including a request that ends in a 304. A 304 saves sending the body.

Only a 200 response gets a tag or a 304. The filter sends any other status unchanged, whatever `If-None-Match` says. A request refused by authorization or rate limiting is never answered 304. When the handler throws, the filter sends the error response unchanged, with no tag.

## Setting a validator in the handler

A handler can set `ETag` on the response. The filter then does not hold the body or compute a hash. It compares the request with the handler's tag.

::: warning
The handler's tag must include the double quotes. An unquoted tag never matches when a client sends it back, so every request gets 200 and nothing reports it.
:::

A handler can also set `Last-Modified`, which the filter compares with `If-Modified-Since`. Only an `ETag` stops the filter from holding the body. When the handler sets only `Last-Modified`, the filter still holds and hashes the body. The response then carries the computed `ETag` as well as the handler's `Last-Modified`.

The handler in `src/Todos/DocumentController.cs` sets both headers through an `IExecutionContext` parameter:

```csharp
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Conditional;
using Hardened.Web.Runtime.Headers;
using Hardened.Web.Runtime.Responses;

namespace Todos;

public class DocumentController
{
    [Get("/documents/{id}")]
    [ConditionalGet]
    public async Task<Response<Document, NotFound>> Read(
        IDocumentStore documents,
        string id,
        IExecutionContext context
    )
    {
        var document = await documents.Find(id);

        if (document is null)
        {
            return new NotFound("document", $"No document has id {id}.");
        }

        context.Response.Headers[KnownHeaders.ETag] = $"\"{document.Version}\"";
        context.Response.Headers[KnownHeaders.LastModified] = HttpDate.Format(document.UpdatedAt);

        return document;
    }
}
```

It reads from the store in `src/Todos/DocumentStore.cs`:

```csharp
using DependencyModules.Runtime.Attributes;

namespace Todos;

public record Document(string Id, string Text, int Version, DateTimeOffset UpdatedAt);

public interface IDocumentStore
{
    Task<Document?> Find(string id);
}

[SingletonService]
public class DocumentStore : IDocumentStore
{
    private readonly Dictionary<string, Document> _documents = new()
    {
        ["a"] = new Document("a", "Hello", 7, new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)),
    };

    public Task<Document?> Find(string id) =>
        Task.FromResult(_documents.TryGetValue(id, out var document) ? document : null);
}
```

Requests for document `a` get the handler's tag and date:

```http
GET /todos/documents/a

HTTP/1.1 200 OK
Content-Type: application/json
ETag: "7"
Last-Modified: Tue, 01 Sep 2026 12:00:00 GMT

{"id":"a","text":"Hello","version":7,"updatedAt":"2026-09-01T12:00:00+00:00"}
```

```http
GET /todos/documents/a
If-None-Match: "7"

HTTP/1.1 304 Not Modified
ETag: "7"
Last-Modified: Tue, 01 Sep 2026 12:00:00 GMT
```

The example uses three Hardened types to set the headers:

| Type | Namespace |
|---|---|
| `IExecutionContext` | `Hardened.Requests.Abstract.Execution` |
| `KnownHeaders` | `Hardened.Requests.Abstract.Headers` |
| `HttpDate` | `Hardened.Web.Runtime.Headers` |

`HttpDate.Format(DateTimeOffset)` writes the date in the format `Last-Modified` uses, in GMT.

A handler that returns `Ok<T>` with an `ETag` header gives the filter its tag the same way. [Declared responses](/guide/responses) covers that type.

## How a request is matched

The filter checks one of two request headers:

| Request header | Checked when | Answers 304 when |
|---|---|---|
| `If-None-Match` | It is present | One of its tags matches the response's `ETag` by weak comparison, or it is `*` |
| `If-Modified-Since` | `If-None-Match` is absent | The response's `Last-Modified` is at or before the date |

A request with a non-matching `If-None-Match` gets a 200, even when its `If-Modified-Since` would match. The filter follows the order in RFC 9110, section 13.2.1.

`If-None-Match: W/"7"` matches `ETag: "7"`, and `If-None-Match: "7"` matches `ETag: W/"7"`. A request can send several tags in `If-None-Match`, separated by commas or in several headers. The header matches when any of its tags matches.

The filter compares `If-Modified-Since` to the second, so a `Last-Modified` in the same second counts as unchanged. The filter also accepts the RFC 850 and asctime date forms in `If-Modified-Since`. It ignores a value that is not a date and sends the 200.

A response without `Last-Modified` never matches `If-Modified-Since`. The filter does not add a `Last-Modified` when it computes a tag.

## The 304 response

The filter removes `Content-Type`, `Content-Length` and `Content-Encoding` from a 304. Every other header that the 200 would have had stays, including `ETag`, `Last-Modified`, `Cache-Control`, `Vary` and the handler's own headers. The 304 to a HEAD request has no `Content-Length` either.

## With the response cache

`[CacheResponse<T>]` puts the same SHA-256 tag as the filter on every response it stores, unless the handler set one. With both attributes on a handler, a request whose `If-None-Match` matches a stored entry gets a 304. The handler does not run. The response cache does not send the stored body. [The execution pipeline](/guide/execution-pipeline) gives the order of the two filters.

The response cache needs a store, which [Response caching](/guide/response-caching) covers. The example runs with `[HardenedMemoryResponseCache]` on `TodosLibrary`.

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Runtime.Caching;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Caching;
using Hardened.Web.Runtime.Conditional;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

public class TodoController
{
    [Operation("getTodo")]
    [Get("/{id}")]
    [CacheResponse<VaryByRoute>(Duration = 3600)]
    [ConditionalGet]
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

The requests below go to a freshly started application, in this order. The third request matches the stored entry and gets a 304. The handler would have answered it with a 404.

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
If-None-Match: "+x0430/JrxmEstC33HEuf0Yg/61Cvwmwtx2WMTybq/s="

HTTP/1.1 304 Not Modified
ETag: "+x0430/JrxmEstC33HEuf0Yg/61Cvwmwtx2WMTybq/s="
```

## With compression

A tag that the filter computes covers the bytes as sent. A client that accepts gzip gets a different strong tag from a client that does not. Each client gets a 304 for its own tag and a 200 for the other's.

A tag set before compression, by the handler or by the response cache, becomes weak on a compressed response. The tag `"7"` is sent as `W/"7"`. A client that sends back `W/"7"` or `"7"` gets a 304, because the comparison is weak. The 304 for a compressed response keeps its `Vary: Accept-Encoding` header.

[Compression](/guide/compression) covers turning compression on and the rest of what it does to a response.

## In a contract-first project

In an OpenAPI-first or Smithy-first project, put `[ConditionalGet]` on the module class. It covers the described GET operations. The served document then lists the 304 and the two request headers on each GET operation, and not on the others. The attribute does not change the contract file.

Do not declare `ETag` as a response header on an operation's 200 in the contract. A header declared on a success response becomes a member of the generated success case. The handler then has to fill it. In a project made from the template with `--contract openapi`, `headers: ETag` on the 200 of `getTodo` in `src/Todos/contracts/todos.yaml` makes the generated case `GetTodoOk(Todo Body, string ETag)`. The template's `return todo;` then fails with `CS0029`:

```text
src/Todos/TodoService.cs(62,16): error CS0029: Cannot implicitly convert type 'Todos.Models.Todo' to 'Todos.Models.GetTodoResponse'
```

[Generating from OpenAPI](/guide/openapi) and [Generating from Smithy](/guide/smithy) cover contract-first projects.

## Limits

The filter does not evaluate `If-Match` or `If-Unmodified-Since`. No request gets a 412.

Nothing skips the handler based on a validator known before the handler runs. Only a response cache hit skips the handler.

## Next

- [Response caching](/guide/response-caching): the response cache and its store
- [Compression](/guide/compression): turning on response compression
- [The execution pipeline](/guide/execution-pipeline): filter order
- [The OpenAPI document](/guide/openapi-document): the OpenAPI document the application serves

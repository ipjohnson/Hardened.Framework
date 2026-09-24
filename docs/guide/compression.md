# Compression

`[Compress]` on a handler compresses its response for a request whose `Accept-Encoding` names gzip
or Brotli. Every web application also decodes a compressed request body, with no declaration.

```csharp
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Compression;

namespace Todos;

public class TodoController
{
    [Operation("listTodos")]
    [Get("/")]
    [Compress]
    public Task<IReadOnlyList<Todo>> All(ITodoStore store) => store.All();
}
```

```http
GET /todos
Accept-Encoding: gzip, br

HTTP/1.1 200 OK
Content-Type: application/json
Content-Encoding: gzip
Vary: Accept-Encoding
```

The body is the `hardened-web` template's two todos as JSON, compressed with gzip. The compressed
response carries `Content-Encoding` and `Vary: Accept-Encoding`. The same request without
`Accept-Encoding` gets the JSON uncompressed:

```http
GET /todos

HTTP/1.1 200 OK
Content-Type: application/json

[{"id":1,"title":"Read the generated code","done":true},{"id":2,"title":"Add an endpoint","done":false}]
```

`[Compress]` is in the namespace `Hardened.Web.Runtime.Compression`, in the package
`Hardened.Web.Runtime`. Nothing else has to be installed or registered.

## Where to declare it

| Declaration | Covers |
|---|---|
| `[Compress]` on a handler method | That handler |
| `[Compress]` on a controller class | Every handler in the class |
| `[Compress]` on a `[HardenedModule]` class | Every handler compiled in the module's project. Not the framework's handlers: `/health/live`, `/health/ready`, `/openapi.json` and `/docs` |
| `[Enable<ResponseCompression>]` on a module class | Every handler in the application, the framework's handlers included |

Without one of these declarations, responses are not compressed. The OpenAPI document is the one
exception. [What is compressed](#what-is-compressed) describes it.

The library module in `src/Todos` carries `[Compress]`:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Compression;
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
[Compress]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);
    }
}
```

The application module in `src/Todos.Host` carries `[Enable<ResponseCompression>]`:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Runtime.Compression;
using Hardened.Web.Runtime.OpenApi;

namespace Todos.Host;

[HardenedModule]
[KestrelRuntime]
[HardenedOpenApiUi(Title = "Todos", Environments = "development")]
[Enable<ResponseCompression>]
[TodosLibrary]
public partial class Application;
```

`ResponseCompression` is in `Hardened.Web.Runtime.Compression`. `[Enable<T>]` is in
`Hardened.Shared.Runtime.Attributes`.

A handler's own `[Compress]`, on its method or its class, replaces the module's `[Compress]` for
that handler. `[Enable<ResponseCompression>]` leaves alone a handler that declares `[Compress]` or
`[Compress<T>]` on its method, its class or its module.
[Deciding per response](#deciding-per-response) describes `[Compress<T>]`.

`[Compress]` on a method and on its class is build error `HRDW003`. So is `[Compress]` beside
`[Compress<T>]` on one method:

```console
CSC : error HRDW003: 'TodoController.All' carries 2 [Compress] declarations - on the method and on its class, or both [Compress] and [Compress<T>]. One declaration decides how an operation is compressed, so remove the others.
```

[Diagnostics](/reference/diagnostics) lists `HRDW003`.

::: warning
A module's `[Compress]` and a handler's `[Compress<T>]` are both installed on the handler. So are a
module's `[Compress<T>]` and a handler's `[Compress]`. The module's declaration runs first and
decides. The handler's declaration has no effect. The build reports nothing. When the module and the
handler both carry `[Compress]`, the handler's declaration replaces the module's.
:::

## Choosing the coding

By default the application offers gzip, then Brotli (`br`). It uses the first of those that the
request's `Accept-Encoding` names. It ignores the order of the codings in the header. It also
ignores quality values.

| Request's `Accept-Encoding` | Response |
|---|---|
| `br, gzip` | gzip |
| `gzip;q=0` | gzip |
| `*` | Uncompressed |
| Only `identity`, `deflate` or `x-gzip` | Uncompressed |

`Favor` on `[Compress]` or `[Compress<T>]` tries one coding first for its handler:

| `Favor` | Order for the handler |
|---|---|
| `CompressionType.Default`, the default | The configured order |
| `CompressionType.GZip` | gzip first |
| `CompressionType.Br` | Brotli first |

```csharp
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Compression;

namespace Todos;

public class TodoController
{
    [Operation("listTodos")]
    [Get("/")]
    [Compress(Favor = CompressionType.Br)]
    public Task<IReadOnlyList<Todo>> All(ITodoStore store) => store.All();
}
```

```http
GET /todos
Accept-Encoding: gzip, br

HTTP/1.1 200 OK
Content-Type: application/json
Content-Encoding: br
Vary: Accept-Encoding
```

The same handler answers `Content-Encoding: gzip` to `Accept-Encoding: gzip`.

`Favor` can only pick a coding that the configuration offers. With [`Encodings`](#configuration) set
to gzip alone, a handler with `Favor = CompressionType.Br` answers gzip to a request that accepts
both. It sends the response uncompressed to a request that accepts only `br`.

## Deciding per response

`[Compress<TPredicate>(args)]` asks a predicate, for each response, whether to compress it.
`TPredicate` implements `ICompressionPredicate`, from `Hardened.Web.Runtime.Compression`. The
interface has two members:

| Member | Receives |
|---|---|
| `static ICompressionPredicate Create(object[] args)` | The attribute's arguments |
| `bool ShouldCompress(object value, IExecutionContext context)` | The value that the handler returned |

`ShouldCompress` in `ListLargerThan` returns `true` for a collection with more items than a given
count:

```csharp
using System.Collections;
using Hardened.Requests.Abstract.Execution;
using Hardened.Web.Runtime.Compression;

namespace Todos;

public sealed class ListLargerThan : ICompressionPredicate
{
    private readonly int _count;

    private ListLargerThan(int count)
    {
        _count = count;
    }

    public static ICompressionPredicate Create(object[] args)
    {
        if (args is [int count])
        {
            return new ListLargerThan(count);
        }

        throw new ArgumentException("ListLargerThan takes one integer.");
    }

    public bool ShouldCompress(object value, IExecutionContext context) =>
        value is ICollection { Count: var count } && count > _count;
}
```

`[Compress<ListLargerThan>(50)]` on `All` passes 50 to `Create`:

```csharp
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Compression;

namespace Todos;

public class TodoController
{
    [Operation("listTodos")]
    [Get("/")]
    [Compress<ListLargerThan>(50)]
    public Task<IReadOnlyList<Todo>> All(ITodoStore store) => store.All();
}
```

With the template's two todos, the response goes out uncompressed:

```http
GET /todos
Accept-Encoding: gzip, br

HTTP/1.1 200 OK
Content-Type: application/json

[{"id":1,"title":"Read the generated code","done":true},{"id":2,"title":"Add an endpoint","done":false}]
```

With 51 todos, the same request gets `Content-Encoding: gzip`. With 50 todos, it does not.

The arguments keep their types: `50` arrives as an `int`. `Create` runs once for each handler, when
the handler's filter chain is built on its first request. The returned instance serves every request
to that handler, so it must not hold anything from one request.

For a `Task<IReadOnlyList<Todo>>` and for a `Response<List<Todo>, NotFound>`, the value that
`ShouldCompress` receives is the `List<Todo>` inside. `ShouldCompress` also receives the value of an
error response, such as the `RequestValidationError` of a validation 400. It runs only when the
request accepts an offered coding.

The predicate replaces the media-type rule for its handler, so it can compress a media type that the
rule leaves out. [What is compressed](#what-is-compressed) describes the rule. A response with no
value is decided by the media-type rule. A response that the response cache replays has no value.

When `Create` throws, every request to the handler gets a 500. The log names the handler:
`[Compress<ListLargerThan>] on GET /todos could not build its predicate: ListLargerThan takes one integer.`
The build does not check the arguments.

## What is compressed

`ResponseCompressionFilter` decides whether to compress a response when the body is first written.
It uses the status, the content type and the handler's value. Without a predicate, the response's
media type decides. The filter ignores parameters such as `charset`. It checks the media type
against this default list:

| Media type | Compressed |
|---|---|
| `application/json`, `application/problem+json` and any `application/*+json` | Yes |
| `application/xml` and any `application/*+xml` | Yes |
| `application/javascript` | Yes |
| `application/x-ndjson` | Yes |
| `image/svg+xml` | Yes |
| `text/*` | Yes, except `text/event-stream` |
| Any other type, such as `image/png`, `application/octet-stream` or `application/wasm` | No |

A 204, a 206, a 304 and a response that already carries `Content-Encoding` are never compressed,
whatever the list or a predicate says. An error response is compressed like any other. That
includes a 400, 404, 409 or 500 with a JSON body.

The OpenAPI document at `/openapi.json` compresses itself. A request that accepts gzip gets it with
`Content-Encoding: gzip`, with or without a declaration.
[The OpenAPI document](/guide/openapi-document) covers it.

A stream of NDJSON items is compressed, and an event stream is not.
[Streaming responses](/guide/streaming) covers both.

## Headers on a compressed response

The filter sets or changes these headers on a response that it compresses:

| Header | On a compressed response |
|---|---|
| `Content-Encoding` | `gzip` or `br` |
| `Vary` | `Accept-Encoding` is added to the values other filters wrote |
| `Content-Length` | Removed. The host frames the compressed body |
| `ETag` | A strong tag becomes weak: `"7"` is sent as `W/"7"` |

A response that the filter does not compress keeps its headers. It gets no `Vary: Accept-Encoding`.
A strong `ETag` on it stays strong.

CORS and `VaryByHeader` add to `Vary` the same way, so each value appears once. [CORS](/guide/cors)
and [Response caching](/guide/response-caching) cover those.

## Compressed request bodies

Every web application decodes a request body sent with `Content-Encoding: gzip` or
`Content-Encoding: br`. Nothing has to be declared. The application decodes the body for every
handler, the framework's included, before the body is bound. JSON,
`application/x-www-form-urlencoded` and `multipart/form-data` bodies are all decoded.

```http
POST /todos
Content-Type: application/json
Content-Encoding: gzip

HTTP/1.1 201 Created
Content-Type: application/json
Location: /todos/3

{"id":3,"title":"Water the plants","done":false}
```

The request body is `{"title":"Water the plants"}` compressed with gzip to 48 bytes. The handler
sees the decoded body. The application removes `Content-Encoding` and `Content-Length` from the
request.

| Request's `Content-Encoding` | Result |
|---|---|
| `gzip` or `br` | The body is decoded |
| `identity` | Counts as no coding |
| Any other coding | 415 with `Accept-Encoding: gzip, br` |
| Two codings, such as `gzip, br` | 415. The message names the whole header value |

A request with no body gets the 415 too: `GET /todos/1` and `GET /health/live` with
`Content-Encoding: deflate` answer 415.

```http
POST /todos
Content-Type: application/json
Content-Encoding: deflate

HTTP/1.1 415 Unsupported Media Type
Content-Type: application/json
Accept-Encoding: gzip, br

{"type":"BadContentEncodingException","message":"deflate is not a supported Content-Encoding","details":""}
```

The decoded body is capped at 30,000,000 bytes. The application answers 413 when it reads past the
cap. A body that decodes to exactly the cap is read in full.

```http
POST /todos
Content-Type: application/json
Content-Encoding: gzip

HTTP/1.1 413 Payload Too Large
Content-Type: application/json

{"type":"DecompressedBodyTooLargeException","message":"The request body decodes to more than 30000000 bytes.","details":""}
```

The request body is 30,178 bytes of gzip that decode to a 31,000,012-byte JSON object.

The response cache's `ByPayload` key is read from the decoded body. The same JSON sent plain, with
gzip and with Brotli makes one cache entry. [Response caching](/guide/response-caching) covers
`ByPayload`.

`Encodings` does not limit decoding. A Brotli body is decoded when `Encodings` holds gzip alone.

A request whose body is not valid in the coding that its header names gets a 500. The application
logs the request as failed.

## Configuration

`services.ConfigureCompression(...)` changes the configuration. It is an extension method in
`Hardened.Web.Runtime.Compression`. This library module calls it in `ConfigureServices`:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Compression;
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

        services.ConfigureCompression(compression =>
        {
            compression.Encodings = ["br", "gzip"];
            compression.MediaTypes.Add("application/wasm");
            compression.MaxDecompressedRequestBytes = 8_000_000;
        });
    }
}
```

With this module and `[Compress]` on `All`, `GET /todos` with `Accept-Encoding: gzip, br` gets
`Content-Encoding: br`. A `[Produces("application/wasm")]` handler that returns `byte[]` is
compressed. A gzip body that decodes to 9,000,012 bytes gets a 413 with the message "The request
body decodes to more than 8000000 bytes."

`ConfigureCompression` changes the defaults rather than replacing them. After
`MediaTypes.Add("application/wasm")`, JSON is still compressed. The configuration applies whether or
not anything compresses responses. The example's cap applies to `POST /todos`, which has no
`[Compress]`.

| Property | Default | Meaning |
|---|---|---|
| `Encodings` | `["gzip", "br"]` | The codings offered for responses, in the application's order of preference. Only `gzip` and `br` are supported |
| `Level` | `CompressionLevel.Fastest` | The level both encoders use. `CompressionLevel` is in `System.IO.Compression` |
| `MediaTypes` | The list under [What is compressed](#what-is-compressed) | Patterns: an exact media type, `type/*` or `type/*+suffix` |
| `ExcludedMediaTypes` | `["text/event-stream"]` | Exact media types that are not compressed even when a pattern matches |
| `MaxDecompressedRequestBytes` | `30_000_000` | The most a compressed request body may decode to |

## Cached and conditional responses

The compression filter runs outside the response cache and inside conditional requests.
[The execution pipeline](/guide/execution-pipeline) gives the order.

The response cache stores the body uncompressed. It never stores `Content-Encoding`. The filter
decides for each request whether to compress a cache hit. A cache hit has no handler value, so the
predicate is not asked. The media-type rule decides instead. A response that the predicate declined
on the miss is compressed on the hit.

The response cache stores the headers of the response that filled it, after the filter changed them.
When a compressed response fills the entry, every hit carries the weak `ETag` and
`Vary: Accept-Encoding`. The hits that go out uncompressed carry them too.

A 304 is not compressed. It keeps `Vary: Accept-Encoding`.

`[ConditionalGet]` sees the compressed bytes, so the tag that it computes differs for gzip, Brotli
and an uncompressed response of the same content. A request gets a 304 only for the tag of its own
coding.

[Response caching](/guide/response-caching) and [Conditional requests](/guide/conditional-requests)
cover those features.

## Limits

- The only codings are gzip and Brotli, for responses and for request bodies. `deflate` and `zstd`
  are not supported.
- Quality values in `Accept-Encoding` are ignored.
- There is no minimum size. The template's 104-byte list goes out as 101 bytes of gzip. A 33-byte
  body goes out as 51 bytes.

## Next

- [Response caching](/guide/response-caching): storing responses, and `VaryByHeader`
- [Conditional requests](/guide/conditional-requests): `ETag` and 304 responses
- [The execution pipeline](/guide/execution-pipeline): the order the filters run in
- [Streaming responses](/guide/streaming): compressing a stream

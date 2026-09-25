# Content negotiation

`[Produces]` names the media types that an operation answers with. When an operation declares two
or more, the request's `Accept` header picks one.

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class TodoController
{
    [Operation("listTodos")]
    [Get("/")]
    [Produces("application/json", "text/csv")]
    public Task<IReadOnlyList<Todo>> All(ITodoStore store) => store.All();
}
```

```http
GET /todos

HTTP/1.1 200 OK
Content-Type: application/json

[{"id":1,"title":"Read the generated code","done":true},{"id":2,"title":"Add an endpoint","done":false}]
```

```http
GET /todos
Accept: text/csv

HTTP/1.1 200 OK
Content-Type: text/csv

id,title,done
1,Read the generated code,True
2,Add an endpoint,False
```

`CsvResponseSerializer` writes the `text/csv` response.
[Writing a serializer](#writing-a-serializer) shows its code. An operation that declares nothing
answers JSON.

## Declaring what an operation produces

`[Produces]` is in the `Hardened.Requests.Abstract.Attributes` namespace, in the
`Hardened.Requests.Abstract` package. It takes one media type or several, in the order that the
operation prefers them. A media type can be a constant, such as `KnownContentType.Json` from
`Hardened.Requests.Abstract.Headers`.

A declaration goes on a method, on its class or on the handler's assembly. An application can also
register a `ResponseContentTypeDefault`, from `Hardened.Requests.Abstract.Serializer`. The nearest
declaration wins, in this order:

| Declared on | Written as |
|---|---|
| The method | `[Produces("text/csv")]` |
| Its class | `[Produces("text/csv")]` on the class |
| The handler's assembly | `[assembly: Produces("text/csv")]` |
| The application | `services.AddSingleton(new ResponseContentTypeDefault("text/csv"))` |

Declarations are not combined. `[assembly: Produces]` covers the handlers compiled in that assembly.
Declared in the host project, it does not cover the library's handlers. A registered default covers
every handler, in every assembly, that declares nothing on its method, its class or its assembly.

The OpenAPI document reads the declarations on the method and the class only. An operation covered
by `[assembly: Produces]` or by a registered default appears in the document under
`application/json` alone. [The OpenAPI document](/guide/openapi-document) covers what a declared
operation publishes.

A contract declares the media types of an operation with the `content:` keys of its responses.
[Generating from OpenAPI](/guide/openapi) covers contracts.

## Return types

The return type and the declared media types decide how the response is written:

| The handler returns | It declares | The response |
|---|---|---|
| A model | Nothing | JSON |
| A model | One or more media types | Written by the serializer for the media type that the request gets |
| `string` | Nothing | Written by the JSON serializer as a JSON string, such as `"Hello"` |
| `string` | One or more media types | The string unchanged, under the media type that the request gets |
| `byte[]` or `Stream` | One media type (`HRDR011` without one) | The bytes unchanged, under that media type |
| `byte[]` or `Stream` | Two or more media types | The bytes unchanged, under the content type that the handler sets, or `application/octet-stream` |
| `IAsyncEnumerable<T>` | Anything | Framed by the streaming filter |

::: warning
A handler that returns `string` and declares `application/json` sends the string unchanged, not as
a JSON string. To send JSON, return a model or declare nothing.
:::

A handler that returns `byte[]` or `Stream` must declare its media type on the method or on its
class. Without one, the build fails with `HRDR011`. `[assembly: Produces]` does not satisfy the
check. The check applies to the success case of a response set. `Response<byte[], NotFound>` needs
`[Produces]` too. For a handler `ExportController.Export` that returns `byte[]` and declares
nothing, the build reports:

```text
CSC : error HRDR011: 'ExportController.Export' answers with byte[] or Stream and carries no [Produces], so nothing says what the bytes are. Answering with either means the handler writes its own response, and no serializer is consulted - declare the media type with [Produces("application/pdf")] or answer with a model.
```

A handler that returns `byte[]` or `Stream` and declares two or more media types is not negotiated.
[Choosing the content type per request](#choosing-the-content-type-per-request) shows how a handler
sets the content type.

[Streaming responses](/guide/streaming) covers a handler that returns `IAsyncEnumerable<T>`. A
handler with `[Output<T>]` renders a view whatever `Accept` says. [Views](/guide/views) covers it.

## Matching the `Accept` header

Under the default mode, `Strict`, the operation decides whether `Accept` is read:

| The operation | `Accept` is read | A request that names none of its media types |
|---|---|---|
| Declares nothing | No | Answered with JSON |
| Returns `string`, `byte[]` or `Stream`, and declares one media type | No | Answered with that media type |
| Returns `byte[]` or `Stream`, and declares two or more | No | Answered with the content type the handler sets, or `application/octet-stream` |
| Returns a model and declares one media type | Yes | 406 |
| Returns a model or `string`, and declares two or more | Yes | 406 |

A contract operation that declares one text media type is matched like a model. It answers 406 to
`Accept: application/json`.

The entries of the header are tried in the order that the client wrote them. The first entry that
the operation produces wins. Parameters on an entry are ignored, including `q`. Media types are
compared without regard to case. The response's `Content-Type` is the declared media type that
matched. For an operation with `[Produces("application/json", "text/plain")]`:

| `Accept` | Answered with |
|---|---|
| Absent, empty or `*/*` | `application/json`, the first declared type |
| `text/plain` or `TEXT/PLAIN` | `text/plain` |
| `text/*` | `text/plain` |
| `application/*` | `application/json` |
| `text/plain;q=0.5, application/json;q=0.9` | `text/plain`, the first entry |
| `image/png, text/plain` | `text/plain`, the first entry it produces |
| `image/png` | 406 |

A request that names nothing the operation produces gets `406 Not Acceptable`. Its body names the
media types that the operation produces. The body is JSON. The `listTodos` operation from the
opening example answers `Accept: application/xml` like this:

```http
GET /todos
Accept: application/xml

HTTP/1.1 406 Not Acceptable
Content-Type: application/json

{"type":"NotAcceptable","message":"This operation produces application/json, text/csv.","details":"application/json, text/csv"}
```

The OpenAPI document lists no 406. The response to an operation that negotiates carries no
`Vary: Accept` header. On a cached operation, the response cache adds one.
[Response caching](/guide/response-caching) covers it.

## Answering without a 406

`ContentNegotiationMode`, in `Hardened.Requests.Abstract.Serializer`, has two values:

| Mode | A request that names none of the declared media types |
|---|---|
| `Strict` | 406, with a body naming what the operation produces. The default |
| `Lenient` | The declared type when there is one, otherwise JSON |

Under `Lenient`, an operation that declares one media type answers with it without reading
`Accept`. An operation that declares two or more answers a request that names none of them with
JSON. No request gets a 406.

The mode applies to the whole application. Register an `IContentNegotiationPolicy` in the
`ConfigureServices` of a module to set it:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Abstract.Serializer;
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

        services.AddSingleton<IContentNegotiationPolicy>(
            new ContentNegotiationPolicy(ContentNegotiationMode.Lenient)
        );
    }
}
```

With that module, the request from the 406 example gets JSON:

```http
GET /todos
Accept: application/xml

HTTP/1.1 200 OK
Content-Type: application/json

[{"id":1,"title":"Read the generated code","done":true},{"id":2,"title":"Add an endpoint","done":false}]
```

A contract sets `Lenient` with `x-hardened-content-negotiation: lenient` at its root.

## A media type that nothing writes

The build warns with `HRDR012` when a handler returns a model and declares a media type that no
serializer known to the build writes. `application/json` is always known. The build reads what each
serializer writes from `[assembly: WritesContentType]`, in `Hardened.Requests.Abstract.Attributes`.
It does not read `ContentType` from a serializer class, even one in the same project. The build
looks for the attribute in the project and in every assembly that the project references. A
serializer in another project or in a package counts. Without the CSV serializer's
`[assembly: WritesContentType]` line, the build warns for each handler that declares `text/csv`:

```text
CSC : warning HRDR012: 'TodoController.All' declares [Produces("text/csv")] and returns a model, and nothing here writes a model as that media type. Register an IResponseSerializer declaring it, or return string, byte[] or Stream and write the bytes yourself. A host that registers one makes this correct, which is why it is a warning.
```

At run time, when no serializer is registered for the media type, a request to such an operation
gets a 500 with an empty body. The log names `ContentTypeNotProducibleException`: "This operation
declares text/csv and no registered serializer can produce any of them."

## Error bodies

When an operation negotiates, a failed request gets its error body in the representation that the
request negotiated. The serializer receives the error body as the value to write. The value is one
of these types:

| Type | Namespace |
|---|---|
| `NotFound` | `Hardened.Web.Runtime.Responses` |
| `RequestValidationError` | `Hardened.Requests.Runtime.Validation` |
| `ErrorModel` | `Hardened.Requests.Abstract.Errors` |

The `getTodo` operation declares the same two media types as `listTodos`:

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

public class TodoController
{
    [Operation("getTodo")]
    [Get("/{id}")]
    [Produces("application/json", "text/csv")]
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

A request for a missing todo gets its 404 as CSV, with the header row alone:

```http
GET /todos/99
Accept: text/csv

HTTP/1.1 404 Not Found
Content-Type: text/csv

id,title,done
```

A handler that returns `string`, `byte[]` or `Stream` answers its failures as JSON.

`[JsonErrorBodies]` answers every response with a status of 400 or more as JSON, whatever the
request negotiated. Successes are unchanged. The attribute is in
`Hardened.Requests.Abstract.Attributes`. Put it on the module that declares the routes, which is
`TodosLibrary` in the template:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Abstract.Attributes;
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
[JsonErrorBodies]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);
    }
}
```

With the attribute, the 404 is JSON:

```http
GET /todos/99
Accept: text/csv

HTTP/1.1 404 Not Found
Content-Type: application/json

{"resource":"todo","detail":"No todo has id 99.","type":"urn:hardened:problem:not-found","title":"Not Found","status":404}
```

The success is unchanged:

```http
GET /todos/1
Accept: text/csv

HTTP/1.1 200 OK
Content-Type: text/csv

id,title,done
1,Read the generated code,True
```

On the module that declares the routes, the attribute changes the responses and the OpenAPI
document. On the application module in the host project, it changes the responses but not the
document. A contract asks for JSON error bodies with `x-hardened-error-bodies: json` at its root,
as [Generating from OpenAPI](/guide/openapi) describes. The 406 body is JSON with or without the
attribute.

In the OpenAPI document, the error responses of an operation list these media types:

| The handler | Its error responses list |
|---|---|
| Returns `string`, `byte[]` or `Stream` | `application/json` alone |
| Streams its response | `application/json` alone |
| Has `[Output<T>]` | `application/json` alone |
| Is under `[JsonErrorBodies]` | `application/json` alone |
| Any other handler | Its declared media types, in order, then `application/json` when it is not one of them |

The document lists media types this way for the error statuses that the handler declares and for
the ones that the pipeline adds, such as the validation 400. Without `[JsonErrorBodies]`, the
document lists the 404 of `GET /todos/{id}` like this:

```json
"404": {
  "description": "Not Found",
  "content": {
    "application/json": {
      "schema": {
        "$ref": "#/components/schemas/NotFound"
      }
    },
    "text/csv": {
      "schema": {
        "$ref": "#/components/schemas/NotFound"
      }
    }
  }
}
```

## Choosing the content type per request

A handler sets `Response.ContentType` on its `IExecutionContext` parameter to choose the content
type of one response. The interface is in `Hardened.Requests.Abstract.Execution`.
[The execution pipeline](/guide/execution-pipeline) covers it.

```csharp
using System.Text;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class ExportController
{
    [Get("/export")]
    [Produces("text/csv", "text/tab-separated-values")]
    public async Task<string> Export(
        ITodoStore store,
        IExecutionContext context,
        [FromQueryString] string format = "csv"
    )
    {
        var separator = format == "tsv" ? "\t" : ",";

        context.Response.ContentType = format == "tsv" ? "text/tab-separated-values" : "text/csv";

        var text = new StringBuilder($"id{separator}title\n");

        foreach (var todo in await store.All())
        {
            text.Append($"{todo.Id}{separator}{todo.Title}\n");
        }

        return text.ToString();
    }
}
```

```http
GET /todos/export

HTTP/1.1 200 OK
Content-Type: text/csv

id,title
1,Read the generated code
2,Add an endpoint
```

The value that the handler sets wins over `Accept`:

```http
GET /todos/export?format=tsv
Accept: text/csv

HTTP/1.1 200 OK
Content-Type: text/tab-separated-values

id	title
1	Read the generated code
2	Add an endpoint
```

Declare every media type that the handler can set. The OpenAPI document lists the declared types
for the operation. A handler that returns `byte[]` or `Stream` and declares two or more media types
must set one. When a handler returns a model and sets a media type that no registered serializer
writes, the response is a 500 with an empty body.

## Writing a serializer

A serializer implements `IResponseSerializer`, from `Hardened.Requests.Abstract.Serializer`.
`[SingletonService]` registers it. `src/Todos/CsvResponseSerializer.cs` writes the `text/csv`
responses on this page:

```csharp
using System.Text;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Serializer;

[assembly: WritesContentType("text/csv")]

namespace Todos;

[SingletonService]
public class CsvResponseSerializer : IResponseSerializer
{
    public bool IsDefaultSerializer => false;

    public string ContentType => "text/csv";

    public async Task SerializeResponse(IExecutionContext context)
    {
        context.Response.ContentType = ContentType;

        IEnumerable<Todo> rows = context.Response.ResponseValue switch
        {
            Todo todo => [todo],
            IEnumerable<Todo> todos => todos,
            _ => [],
        };

        var csv = new StringBuilder("id,title,done\n");

        foreach (var row in rows)
        {
            csv.Append($"{row.Id},{row.Title},{row.Done}\n");
        }

        await context.Response.Body.WriteAsync(
            Encoding.UTF8.GetBytes(csv.ToString()),
            context.CancellationToken
        );
    }
}
```

| Member | Description |
|---|---|
| `ContentType` | The media type it writes, and the key it is found by |
| `IsDefaultSerializer` | Whether it writes for an operation that declares nothing, and for a `Lenient` request that matches nothing. The JSON serializer returns `true` |
| `CanProduce(string mediaType, IExecutionContext context)` | Whether it can write this response as that media type. The default implementation compares `mediaType` with `ContentType` |
| `SerializeResponse(IExecutionContext context)` | Writes `context.Response.ResponseValue` to `context.Response.Body` |

A serializer is found by its `ContentType`. It writes the responses of every operation that
declares that media type. It also writes their error bodies, as [Error bodies](#error-bodies)
describes. `[assembly: WritesContentType("text/csv")]` stops `HRDR012` for the operations that
declare `text/csv`.

`SerializeResponse` sets `context.Response.ContentType` itself. Under `Lenient`, an operation that
declares one media type calls its serializer directly. Nothing else sets the header for that
response.

When two serializers are registered for one media type, the one registered last writes the
response. A serializer for `application/json` that `TodosLibrary` registers, with
`[SingletonService]` or in its `ConfigureServices`, does not replace the framework's. Registered in
`ConfigureServices` on the host's `Application`, it does. [JSON serialization](/guide/json) covers
replacing the JSON serializer.

## Request bodies

The request's `Content-Type` selects the deserializer that reads a body parameter. `[Produces]` has
no effect on request bodies. The JSON deserializer reads a body whose `Content-Type` contains
`application/json`. It also reads every body that no other deserializer claims, including a body
with no `Content-Type`.

A request with a body that is not JSON, under a `Content-Type` that nothing claims, gets a 400 with
the code `invalid` and the parser's message:

```http
POST /todos
Content-Type: application/xml

<todo><title>Buy milk</title></todo>

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"request","code":"invalid","message":"'<' is an invalid start of a value."}]}
```

A handler with a body parameter gets no 415 for its `Content-Type`. The JSON deserializer reads a
JSON body sent as `text/plain`:

```http
POST /todos
Content-Type: text/plain

{"title":"Buy milk"}

HTTP/1.1 201 Created
Content-Type: application/json
Location: /todos/3

{"id":3,"title":"Buy milk","done":false}
```

The MessagePack package adds a deserializer for `application/x-msgpack`.
[MessagePack](/guide/message-pack) covers it. A `[FromForm]` parameter is read as a form, and a
body that is not a form answers 415. [Forms and files](/guide/forms) covers it. A body parameter declared as `byte[]` or `Stream`
receives the body as sent. [Parameter binding](/guide/parameter-binding) covers it.

A deserializer implements `IRequestDeserializer`, from `Hardened.Requests.Abstract.Serializer`.
`[SingletonService]` registers it.

| Member | Description |
|---|---|
| `CanProcessContext(IExecutionContext context)` | Whether it reads this request's body |
| `DeserializeRequestBody<T>(IExecutionContext context)` | Reads the body as `T` |
| `IsDefaultSerializer` | Whether it reads a body that no deserializer claimed. The JSON deserializer returns `true` |
| `Order` | When it is asked, relative to the others. The default is `RequestDeserializerOrder.Normal` |

Deserializers are asked in `Order`, lowest first. The first one whose `CanProcessContext` returns
`true` reads the body. Among deserializers with the same `Order`, the one registered last is asked
first. The values of `RequestDeserializerOrder` are:

| Value | Number | Used by |
|---|---|---|
| `Specialized` | -100 | A deserializer for one content type. The MessagePack deserializer |
| `Normal` | 0 | The JSON deserializer |
| `Deferred` | 1000 | A deserializer asked after the others |

## Limits

`[ContentNegotiation(ContentNegotiationMode.Lenient)]` on a module class fails the build with
`CS1503` in the generated `<Module>.Module.g.cs`: "cannot convert from 'int' to
'Hardened.Requests.Abstract.Serializer.ContentNegotiationMode'". Register the policy instead, as
[Answering without a 406](#answering-without-a-406) shows.
`[assembly: ContentNegotiation(ContentNegotiationMode.Lenient)]` builds and has no effect.

Under `Strict` without `[JsonErrorBodies]`, a client that asks a `string` handler for its declared
text media type gets a 406 in place of the operation's 400, 404 or 500.

## Next

| Page | Covers |
|---|---|
| [JSON serialization](/guide/json) | The JSON serializer and its options |
| [MessagePack](/guide/message-pack) | MessagePack beside JSON |
| [Streaming responses](/guide/streaming) | NDJSON and server-sent events |
| [Views](/guide/views) | Rendering HTML with a view |
| [Response caching](/guide/response-caching) | One cache entry per representation |

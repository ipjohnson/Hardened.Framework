# The OpenAPI document

The build writes an OpenAPI document from the application's handlers: their paths, verbs,
parameters, request bodies and responses. `[Enable<OpenApiDocumentPublishing>]` on the module that
declares the routes serves the document at `/openapi.json`.

This example gives the template's handler in `src/Todos/TodoController.cs` a doc comment of its own:

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

public class TodoController
{
    /// <summary>One todo, or 404.</summary>
    /// <remarks>Answers 404 when no todo has the id.</remarks>
    /// <param name="id">The todo's id.</param>
    [Operation("getTodo")]
    [Get("/{id}")]
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

The document lists the operation under `/todos/{id}`:

```json
"get": {
  "tags": [
    "Todo"
  ],
  "operationId": "getTodo",
  "summary": "One todo, or 404.",
  "description": "Answers 404 when no todo has the id.",
  "parameters": [
    {
      "name": "id",
      "in": "path",
      "required": true,
      "description": "The todo's id.",
      "schema": {
        "type": "integer",
        "format": "int32",
        "minimum": 1
      }
    }
  ],
  "responses": {
    "200": {
      "description": "OK",
      "content": {
        "application/json": {
          "schema": {
            "$ref": "#/components/schemas/Todo"
          }
        }
      }
    },
    "400": {
      "description": "The request failed validation.",
      "content": {
        "application/json": {
          "schema": {
            "$ref": "#/components/schemas/RequestValidationError"
          }
        }
      }
    },
    "404": {
      "description": "Not Found",
      "content": {
        "application/json": {
          "schema": {
            "$ref": "#/components/schemas/NotFound"
          }
        }
      }
    }
  }
}
```

In the example, `operationId` comes from `[Operation]`, `summary` from `<summary>` and
`description` from `<remarks>`. The parameter's `description` comes from `<param>`, and its
`minimum` from `[Range(Min = 1)]`. The 200 and the 404 come from the return type. The build adds
the 400 because `id` is validated.

The template puts `[Enable<OpenApiDocumentPublishing>]` on its library module. `[HardenedOpenApiUi]`
on its application module serves a reference page over the document at `/docs`.

## Serving the document

`OpenApiDocumentPublishing` and `HardenedOpenApiUi` are in the namespace
`Hardened.Web.Runtime.OpenApi`, in the package `Hardened.Web.Runtime`.
`Hardened.Web.SourceGenerator` writes the document.

Put `[Enable<OpenApiDocumentPublishing>]` on the module that declares the routes. The build writes
the document from the routes compiled with that module. This is `src/Todos/TodosLibrary.cs` as the
template writes it, with its comments left out:

```csharp
using DependencyModules.Runtime.Interfaces;
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
    }
}
```

On a module that declares no routes, such as the template's application module, the document is
`"paths": {}`. The build warns `HRDOA003`. With the attribute on both modules, the empty document
answers `/openapi.json`. The template with the attribute added to `Application` as well builds with
this warning:

```console
CSC : warning HRDOA003: 'Application' enables OpenApiDocumentPublishing and declares no routes, so the document served at /openapi.json is "paths": {}. The document is written from the routes in the same compilation as the attribute - move [Enable<OpenApiDocumentPublishing>] to the module that declares them. With it on both, the empty one shadows the real one.
```

The same application answers with the empty document:

```http
GET /openapi.json

HTTP/1.1 200 OK
Content-Type: application/json
Cache-Control: no-cache

{"openapi":"3.2.0","info":{"title":"Application","version":"1.0.0"},"paths":{}}
```

Without the attribute, the build writes no document and the assembly carries none.

The document answers GET and HEAD. The response carries `Cache-Control: no-cache`:

```http
HEAD /openapi.json

HTTP/1.1 200 OK
Content-Type: application/json
Cache-Control: no-cache
```

Another method answers 405 with `Allow: GET, HEAD`:

```http
POST /openapi.json

HTTP/1.1 405 Method Not Allowed
Allow: GET, HEAD
```

A request that sends `Accept-Encoding: gzip` gets the document compressed, with
`Content-Encoding: gzip`. Any other request gets it uncompressed.

The document has no `[AllowAnonymous]`. Under `[RequireAuthorization]` on the application module,
`/openapi.json` answers 401 to a caller with no credentials. [Authorization](/guide/authorization)
covers both attributes.

The document does not describe `/openapi.json`, the reference page or the health endpoints. It
answers ahead of a route that the application declares at the same path.

## Serving the document at another path

`[OpenApiDocumentPath(path)]` on a class makes the class a marker that serves the document at
`path`. `[Enable<T>]` with that class takes the place of `[Enable<OpenApiDocumentPublishing>]`.
`OpenApiDocumentPublishing` is the same kind of marker, with the path `/openapi.json`.
`OpenApiDocumentPath` is in `Hardened.Web.Runtime.OpenApi`. `Enable<T>` is in
`Hardened.Shared.Runtime.Attributes`.

`src/Todos/SpecEndpoint.cs` declares a marker for `/spec.json`:

```csharp
using Hardened.Web.Runtime.OpenApi;

namespace Todos;

[OpenApiDocumentPath("/spec.json")]
public sealed class SpecEndpoint;
```

`src/Todos/TodosLibrary.cs` enables it:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization.Metadata;

namespace Todos;

[HardenedModule]
[HardenedWebModule]
[BasePath("/todos")]
[Server("http://localhost:5080", "Local")]
[Enable<SpecEndpoint>]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);
    }
}
```

The document then answers at `/spec.json`. `/openapi.json` answers 404:

```http
HEAD /spec.json

HTTP/1.1 200 OK
Content-Type: application/json
Cache-Control: no-cache
```

```http
GET /openapi.json

HTTP/1.1 404 Not Found
```

The reference page names the new path in `DocumentPath`, as `src/Todos.Host/Application.cs` does
here:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Runtime.OpenApi;

namespace Todos.Host;

[HardenedModule]
[KestrelRuntime]
[HardenedOpenApiUi(Title = "Todos", DocumentPath = "/spec.json", Environments = "development")]
[TodosLibrary]
public partial class Application;
```

[`<HardenedOpenApiOutput>`](#writing-the-document-to-a-file) writes the document whichever marker
enabled it.

## The reference page

`[HardenedOpenApiUi]` is a module attribute. It serves an HTML page that loads Scalar's API
reference script and renders the document at `DocumentPath`. The template serves the page in the
`development` environment only. [Project templates](/guide/project-templates) covers that choice.

| Property | Default | Sets |
|---|---|---|
| `Path` | `/docs` | Where the page is served |
| `Title` | `API Reference` | The page's `<title>` |
| `DocumentPath` | `/openapi.json` | Where the page fetches the document |
| `Environments` | Not set: every environment | The environments the page is served in |
| `ScriptUrl` | `@scalar/api-reference` 1.65.1, the standalone build on jsDelivr | Where the browser loads the reference UI |
| `ScriptIntegrity` | The `sha384` hash of that build | The script's `integrity` attribute. `""` writes none |
| `DecodeMessagePack` | `false` | Decoding MessagePack response bodies on the page |
| `MessagePackScriptUrl` | `@msgpack/msgpack` 3.1.3, the UMD build on jsDelivr | Where the decoder loads from |

`Environments` is a list of environment names separated by `,` or `;`. The attribute serves the
page where one of them matches the application's environment name, ignoring case. Without
`Environments`, it serves the page in every environment. The [Environments](/guide/environments)
page covers the environment name.

A module can carry the attribute more than once, for one page per `Path`. Two with the same `Path`
serve the first. A `Path` written without a leading slash gets one.

The page answers GET and HEAD, as `text/html; charset=utf-8`. Another method answers 405 with
`Allow: GET, HEAD`. The page has no `[AllowAnonymous]`. Under `[RequireAuthorization]` on the
application module, `/docs` answers 401 to a caller with no credentials.

`ScriptUrl` with `ScriptIntegrity = ""` loads the script from that URL with no `integrity`
attribute. A `ScriptUrl` with `ScriptIntegrity` left unset keeps the default hash. Any other file
fails that hash. Hardened does not serve a copy of the script. The file at a `ScriptUrl` of the
application's own is the application's to serve.

Here `src/Todos.Host/Application.cs` adds a second page over a self-hosted script:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Runtime.OpenApi;

namespace Todos.Host;

[HardenedModule]
[KestrelRuntime]
[HardenedOpenApiUi(Title = "Todos", Environments = "development")]
[HardenedOpenApiUi(
    Path = "/docs/self-hosted",
    Title = "Todos",
    ScriptUrl = "/assets/api-reference.js",
    ScriptIntegrity = ""
)]
[TodosLibrary]
public partial class Application;
```

The two pages answer:

```http
GET /docs

HTTP/1.1 200 OK
Content-Type: text/html; charset=utf-8

<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Todos</title>
</head>
<body>
<script id="api-reference" data-url="/openapi.json"></script>
<script src="https://cdn.jsdelivr.net/npm/@scalar/api-reference@1.65.1/dist/browser/standalone.js" integrity="sha384-G6dkutu2k5IYVyNESLoFIpgaHx38IJTZ/HhrwN0fecTle9te75y8Kru3rJEJ0ZJV" crossorigin="anonymous"></script>
</body>
</html>
```

```http
GET /docs/self-hosted

HTTP/1.1 200 OK
Content-Type: text/html; charset=utf-8

<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Todos</title>
</head>
<body>
<script id="api-reference" data-url="/openapi.json"></script>
<script src="/assets/api-reference.js"></script>
</body>
</html>
```

`DecodeMessagePack = true` makes the page decode MessagePack response bodies in its request panel.
The page then starts Scalar from an inline `<script>`. It loads a decoder from
`MessagePackScriptUrl`. A `script-src` policy without `'unsafe-inline'` blocks the inline script.
[MessagePack](/guide/message-pack) covers the rest.

## Serving a contract's document

In an OpenAPI-first or Smithy-first project, metadata on the item that declares the contract serves
the document and the page. No attribute is involved. `--contract openapi` writes this item in
`src/Todos/Todos.csproj`, shown here without its comments:

```xml
<ItemGroup>
  <HardenedOpenApiSpec Include="contracts\todos.yaml">
    <PublishUrl>/openapi.json</PublishUrl>
    <SourceUrl>/openapi.yaml</SourceUrl>
    <UiUrl>/docs</UiUrl>
    <UiEnvironments>development</UiEnvironments>
  </HardenedOpenApiSpec>
</ItemGroup>
```

| Metadata | Serves |
|---|---|
| `PublishUrl` | The document generated from the contract, as `application/json` |
| `SourceUrl` | The contract file as written |
| `UiUrl` | A reference page over the `PublishUrl` document |
| `UiEnvironments` | Limits the `UiUrl` page to these environments |
| `EmbedDocument` | `false` leaves the contract file out of the assembly |

`PublishUrl` serves the generated document as `application/json` whatever the contract file's
format. The generated document keeps the contract's `info`, `servers` and descriptions. It adds
what a code-first document adds, such as the [validation 400](#the-400-and-404-the-build-adds). It
is OpenAPI 3.2.0 whatever version the contract declares.

`SourceUrl` serves the contract file as written, comments included. The file goes out as
`application/yaml` for a `.yaml` or `.yml` file and `application/json` otherwise. For the
template's `contracts/todos.yaml`, the two URLs answer:

```http
HEAD /openapi.json

HTTP/1.1 200 OK
Content-Type: application/json
Cache-Control: no-cache
```

```http
HEAD /openapi.yaml

HTTP/1.1 200 OK
Content-Type: application/yaml
Cache-Control: no-cache
```

`UiUrl` serves a reference page over the `PublishUrl` document. Its title is `API Reference`.
`UiEnvironments` limits that page to the environments it names, by the rules of `Environments` in
[the reference page](#the-reference-page). A URL written without a leading slash gets one.

The build embeds the contract file in the assembly unless `EmbedDocument` metadata or the
`<HardenedOpenApiEmbedDocument>` property is `false`. Only `SourceUrl` needs it. `UiUrl` without
`PublishUrl` fails the build with `HOAT016`. `SourceUrl` with the contract not embedded fails it
with `HOAT017`. A Smithy project reports the same under `HSMT`.

With neither `PublishUrl` nor `UiUrl`, the application serves no document and no page.
`<HardenedOpenApiOutput>` still writes the document.

In a Smithy project, `PublishUrl`, `UiUrl` and `UiEnvironments` go on a `HardenedSmithyModel` item
and apply to the whole model. Two items with different values fail the build with `HSMT015`.
`SourceUrl` on a `HardenedSmithyModel` item serves nothing, and the build reports nothing. A
`HardenedSmithyAst` item reads it.

[Generating from OpenAPI](/guide/openapi) and [Generating from Smithy](/guide/smithy) cover
declaring the item.

## The title, servers and tags

`info.title` is the class name of the module that serves the document. `info.version` is `1.0.0`.
The template's document is titled `TodosLibrary`.

`[OpenApiInfo(title, version, description)]` on that module sets them. Its `version` defaults to
`1.0.0`, and its `description` is optional. `[Server(url, description)]` on that module adds an
entry to `servers`. It can be repeated, and the entries keep their order. Its `description` is
optional. Without `[Server]`, the document has no `servers`. `OpenApiInfo` and `Server` are in the
namespace `Hardened.Web.Runtime.Attributes`.

Here `src/Todos/TodosLibrary.cs` sets a title and two servers:

```csharp
using DependencyModules.Runtime.Interfaces;
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
[OpenApiInfo("Todos API", "2.1.0")]
[Server("http://localhost:5080", "Local")]
[Server("https://todos.example.com", "Production")]
[Enable<OpenApiDocumentPublishing>]
public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);
    }
}
```

The document starts like this:

```json
{
  "openapi": "3.2.0",
  "info": {
    "title": "Todos API",
    "version": "2.1.0"
  },
  "servers": [
    {
      "url": "http://localhost:5080",
      "description": "Local"
    },
    {
      "url": "https://todos.example.com",
      "description": "Production"
    }
  ],
  "tags": [
    {
      "name": "Todo"
    }
  ],
```

`tags` lists every tag that the [operations](#operations) use.

On any other module, including the template's application module, `[OpenApiInfo]` and `[Server]`
change nothing. `[assembly: Server(...)]` compiles and changes nothing. A contract's own `info` and
`servers` win over both attributes.

The template's `[Server("http://localhost:5080", "Local")]` is the default address of the client
the template generates. [Generated clients](/guide/clients) covers the client.

## Operations

Each field of an operation comes from the handler or its class:

| Field | Comes from | Without it |
|---|---|---|
| `operationId` | `[Operation("id")]` on the method | The method name with its first letter lower-cased: `Documented` publishes `documented` |
| `tags` | `[Tag("name")]` on the controller class | The class name without a `Controller` suffix: `TodoController` publishes `Todo` |
| `summary` | The method's `<summary>` | No `summary` |
| `description` | The method's `<remarks>` | No `description` |
| A parameter's `description` | The method's `<param name="...">` | No `description` |
| `deprecated: true` | `[Obsolete]` on the method or its class | No `deprecated` |

Two handlers without `[Operation]` whose method names match each get their tag in front. `List` on
a controller tagged `Params` publishes `paramsList`, and on one tagged `Body` publishes `bodyList`.
Two handlers that declare the same `[Operation]` id fail the build with `HRDOA004`.

`Operation` and `Tag` are in the namespace `Hardened.Web.Runtime.Attributes`.
[Route links](/guide/route-links) names its link types after the same tag.

## Parameters and request bodies

A value bound from the path, the query string, a header or a cookie is a parameter, with `in` set
to `path`, `query`, `header` or `cookie`. Its name is the name the request uses, such as
`X-Tenant` for `[FromHeader("X-Tenant")] string tenant`. A query string model publishes one query
parameter per member.

A path parameter is always required. Any other parameter is required unless its type is nullable
or it has a default value. The document leaves out a parameter's default value.

The path key carries no route constraint: `/lab/constrained/{id:int}` publishes as
`/todos/lab/constrained/{id}`. A catch-all token, `{*path}`, publishes as `{path}`. Its parameter
carries `"x-hardened-catch-all": true`.

| Body | Publishes |
|---|---|
| A JSON body | A `requestBody` with `required: true`, under `application/json`, as a `$ref` to its model's schema |
| A `byte[]` or `Stream` body | `application/octet-stream` with `{"type": "string", "format": "binary"}` |
| `[FromForm]` fields | One `requestBody` of `application/x-www-form-urlencoded`: an object with one property per field |
| A `[FromForm]` model | A `$ref` to its schema |
| A `[FromForm]` model and fields | The two joined with `allOf` |

A file among the fields, or in a form model, makes the body `multipart/form-data`. A file is
`{"type": "string", "format": "binary"}`, and a list of files is an array of that. A form's
`requestBody` is required when any field or member is required.
[Parameter binding](/guide/parameter-binding) and [Forms and files](/guide/forms) cover the
binding.

Each C# type publishes this schema, in a parameter and in a body:

| C# type | Schema |
|---|---|
| `string`, `char` | `{"type": "string"}` |
| `bool` | `{"type": "boolean"}` |
| `byte`, `sbyte`, `short`, `ushort`, `int`, `uint` | `{"type": "integer", "format": "int32"}` |
| `long`, `ulong` | `{"type": "integer", "format": "int64"}` |
| `float` | `{"type": "number", "format": "float"}` |
| `double` | `{"type": "number", "format": "double"}` |
| `decimal` | `{"type": "number"}` in a parameter, `{"type": "number", "format": "decimal"}` in a body |
| `DateTime`, `DateTimeOffset` | `{"type": "string", "format": "date-time"}` |
| `DateOnly` | `{"type": "string", "format": "date"}` |
| `TimeOnly`, `TimeSpan` | `{"type": "string"}` |
| `Guid` | `{"type": "string", "format": "uuid"}` |
| `Uri` | `{"type": "string", "format": "uri"}` |
| An enum | A `$ref` to the enum's schema, described under [Schemas](#schemas) |
| An array or a list | `{"type": "array", "items": ...}` |
| `object`, in a body | `{}` |

## Responses

An operation's `responses` come from the handler's return type, `SuccessStatus` on the verb
attribute, and `[Throws<T>]`. [Declared responses](/guide/responses) covers what each response type
publishes: one response per status, a `oneOf` for two cases at one status, and the headers a case
carries, such as the `Location` of `Created<T>`.

A handler that returns nothing publishes a 200 with no `content`. A 204 or 304 response has no
`content`. Each response's `description` is the status's reason phrase, such as `OK` or
`Not Found`. The generator describes a status it has no phrase for as `Status` and its number.

The document publishes the body under each media type in `[Produces]`, in that order. Without
`[Produces]`, it publishes the body under `application/json`.
[Content negotiation](/guide/content-negotiation) covers `[Produces]`.

A handler that returns `IAsyncEnumerable<T>` publishes the stream's media type, with the items as
an array under `schema` and one item under `itemSchema`. [Streaming responses](/guide/streaming)
covers the framing.

## The 400 and 404 the build adds

An operation that validates a parameter or its body lists a 400, described "The request failed
validation.", with the `RequestValidationError` schema. [Validation](/guide/validation) covers it.

The build adds the same 400 to an operation that binds a value from the path, the query string, a
header, a cookie or a form into a type other than `string`. An operation that declares a 400 itself
gets no validation 400.

A path token whose route constraint guarantees the conversion, such as `{id:int}` for an `int`,
adds no 400. It adds a 404 with no `content`, described "The path did not name a resource: a token
failed its route constraint." When that operation declares a 404 of its own, its description gains
the sentence "A token that fails its route constraint answers this status too, before the handler
and with no body." [Routing](/guide/routing) covers route constraints.

## Schemas

A class, record, struct or interface publishes a schema under `components/schemas`, named after the
type. Each use is a `$ref` to it. A generic type's name spells its type arguments: `Paged<Todo>`
publishes `PagedOfTodo`. Two different types with one name publish one schema between them. The
build warns `HRDOA005`.

A public instance property with a getter is a property of the schema. A property's name is the one
`[JsonPropertyName]` gives, or its own name with the first letter lower-cased. A type's `<summary>`
is its schema's `description`. A property's `<summary>` is that property's `description`.

`required` lists each member whose type is a non-nullable reference type or a non-nullable value
type, and each member with `[Required]`. A member with a property initializer or a constructor
default is left out. A constructor default publishes as the member's `default`. A property
initializer does not.

A nullable member adds `"null"` to its type: `string?` publishes `{"type": ["string", "null"]}`. A
nullable member whose schema is a `$ref` publishes `{"anyOf": [{"$ref": ...}, {"type": "null"}]}`.

| Type | Schema |
|---|---|
| An enum | `{"type": "string", "enum": [...]}`, holding the values the JSON serializer writes |
| A dictionary | `{"type": "object", "additionalProperties": ...}`. An enum key adds `propertyNames` with a `$ref` to the enum |
| An array, `List<T>`, `IReadOnlyList<T>`, `ICollection<T>`, `IEnumerable<T>`, `HashSet<T>` and their interfaces | An array |
| `byte[]`, `Stream` and `IFormFile` | `{"type": "string", "format": "binary"}` |
| `Task<T>` or `ValueTask<T>`, returned by a handler | `T` |
| A stream of `SseItem<T>` | Its items as `T` |

[JSON serialization](/guide/json) covers the enum values. A member with `[Key(n)]` from the
MessagePack package publishes `"x-message-pack-index": n`. [MessagePack](/guide/message-pack)
covers keys.

A constraint from `ValidationModules.Constraints` publishes its keyword on the member's or the
parameter's schema, such as `minLength` for `[StringLength]`. [Validation](/guide/validation) lists
the keyword of each constraint, and the ones that publish nothing. A constraint on a member whose
schema is a `$ref` publishes nothing. An exclusive bound publishes as the number:
`[Range(0, 1, ExclusiveMin = true)]` publishes `"exclusiveMinimum": 0`, whatever the
[document's version](#the-document-version).

The template declares `NewTodo` in `src/Todos/TodoStore.cs`, shown here without its `<remarks>`:

```csharp
using ValidationModules.Constraints;

namespace Todos;

/// <summary>
/// What a client sends to create one.
/// </summary>
public record NewTodo([property: StringLength(1, 64)] string Title);
```

It publishes this schema:

```json
"NewTodo": {
  "type": "object",
  "description": "What a client sends to create one.",
  "required": [
    "title"
  ],
  "properties": {
    "title": {
      "type": "string",
      "minLength": 1,
      "maxLength": 64
    }
  }
}
```

## Statuses a filter attribute publishes

An attribute that installs a filter publishes the statuses the filter can answer, and the headers it
writes and reads. The build reads the attribute on the method, its class, the module class and the
assembly.

| Declaration | Publishes |
|---|---|
| `[AuthorizeGrants]`, or any attribute implementing `IAuthorizeAttribute` | A 403, "The caller does not hold what this operation requires." |
| `[Authorize<TScheme>]` | A `security` requirement naming `TScheme`, the scheme under `components.securitySchemes`, a 401 with a `WWW-Authenticate` header, and the 403 |
| `[RateLimit]` | A 429 with a `Retry-After` header |
| `[Timeout]` | A 504, or the `Status` it sets, and `x-hardened-timeout` |
| `[ConditionalGet]` | A 304 with an `ETag` header, an `ETag` header on the 200, and optional `If-None-Match` and `If-Modified-Since` header parameters |

The refusals use the `ErrorModel` schema: `type`, `message` and `details`, all strings.

`x-hardened-timeout` is the budget in milliseconds, such as `2000`. With a `Status` other than 504
or a `RetryAfterSeconds`, it is an object:
`{"milliseconds": 1500, "status": 503, "retryAfterSeconds": 5}`.

`[Authorize<TScheme>]` with `[AuthorizeGrants("todos:read")]` on an OAuth2 scheme lists
`todos:read` as the requirement's scope. [Authentication](/guide/authentication) covers the scheme
attributes.

A filter attribute on the implementation of a contract publishes the same way. The 504 of a
`[Timeout]` there comes without `x-hardened-timeout`. Only a budget declared in the contract writes
`x-hardened-timeout`.

A filter added at run time, with `AddGlobalFilter`, `IGlobalFilterRegistry` or
`[Enable<ConditionalGet>]`, publishes nothing. A filter attribute of the application's own publishes
a status with `[AnswersStatus]`. [The execution pipeline](/guide/execution-pipeline) covers
run-time filters, `[AnswersStatus]` and `IAuthorizeAttribute`.

[Rate limiting](/guide/rate-limiting), [Request timeouts](/guide/request-timeouts) and
[Conditional requests](/guide/conditional-requests) cover the attributes themselves.

## Routes registered at startup

The document the application serves includes the routes registered at startup. The file that
`<HardenedOpenApiOutput>` writes does not. [Registered routes](/guide/registered-routes) covers
what such a route publishes.

## The document version

The document is OpenAPI 3.2.0. `<HardenedOpenApiVersion>` in the project that writes the document
sets `3.0.0`, `3.1.0` or `3.2.0`. It also takes `3.0`, `3.1` and `3.2`. Any other value fails the
build with `HRDOA001`. This block in `src/Todos/Todos.csproj` sets 3.1.0:

```xml
<PropertyGroup>
  <HardenedOpenApiVersion>3.1.0</HardenedOpenApiVersion>
</PropertyGroup>
```

Below 3.2, a streamed response has no `itemSchema`. The build warns `HRDOA002` for each handler
that streams.

The property changes nothing else. Body schemas keep the 3.1 forms at 3.0.0: a nullable member
stays `{"type": ["string", "null"]}` and an exclusive bound stays `exclusiveMinimum: 0`. A
contract's document is 3.2.0 whatever the property says.
[`<HardenedOpenApiOutputVersion>`](#writing-the-document-to-a-file) writes a 3.0.0 file that uses
the 3.0 forms.

## Writing the document to a file

`<HardenedOpenApiOutput>` writes the document the application serves to a file after the compile.
A relative path is relative to the project's directory. `.json` writes indented JSON, and `.yaml`
or `.yml` writes YAML.

`<HardenedOpenApiOutputVersion>` set to `3.0.0` or `3.1.0` lowers the file for a reader that
refuses 3.2. The served document does not change. These properties in
`src/Todos/Todos.csproj` write `openapi/Todos.json` at 3.0.0:

```xml
<PropertyGroup>
  <HardenedOpenApiOutput>openapi/Todos.json</HardenedOpenApiOutput>
  <HardenedOpenApiOutputVersion>3.0.0</HardenedOpenApiOutputVersion>
</PropertyGroup>
```

| In the served document | At `3.1.0` | At `3.0.0` |
|---|---|---|
| `"openapi": "3.2.0"` | `"openapi": "3.1.0"` | `"openapi": "3.0.0"` |
| `itemSchema` beside the array under `schema` | Removed. The array stays | Removed. The array stays |
| A numeric `exclusiveMinimum` or `exclusiveMaximum` | Unchanged | The bound as `minimum` or `maximum`, with `exclusiveMinimum: true` or `exclusiveMaximum: true` |
| `{"type": [T, "null"]}` | Unchanged | `{"type": T, "nullable": true}` |
| `{"anyOf": [{"$ref": ...}, {"type": "null"}]}` | Unchanged | `{"allOf": [{"$ref": ...}], "nullable": true}` |
| `propertyNames` | Unchanged | Removed |

The template sets `<HardenedOpenApiOutput>` to `openapi/Todos.json` in `src/Todos/Todos.csproj`
when it writes a client project. It writes one unless `--client none`. The template's client
project generates from the file. [Generated clients](/guide/clients) covers it, and the check that
keeps the file current.

The build task reads the document out of the compiled assembly, so a class library with no entry
point writes the file. The file holds the served document, except the
[routes registered at startup](#routes-registered-at-startup).

The build rewrites the file only when its content changes. When the assembly has not changed, the
target does not run. A design-time build writes nothing.

In a code-first project, the module needs `[Enable<OpenApiDocumentPublishing>]` or a marker of its
own. Without one, the build fails with `HRDOA018`. This is the output for the template with
`[Enable<OpenApiDocumentPublishing>]` removed from `TodosLibrary`:

```console
error HRDOA018: <HardenedOpenApiOutput> is set to 'openapi/Todos.json', but Todos.dll carries no served OpenAPI document. The document is written only for a module that enables publishing, and the export reads that one copy. Add [Enable<OpenApiDocumentPublishing>] to the module that declares the routes, or remove the property.
```

An OpenAPI-first or Smithy-first project always carries the document. Its diagnostic codes start
`HOAT` or `HSMT` where a code-first project's start `HRDOA`. The lowered file names each streaming
operation in warning `030`.

## Diagnostics

| Code | Severity | Reported when |
|---|---|---|
| `HRDOA001` | Error | `<HardenedOpenApiVersion>` is not a version the generator writes |
| `HRDOA002` | Warning | A handler streams and the document is below 3.2 |
| `HRDOA003` | Warning | A module that serves the document declares no routes |
| `HRDOA004` | Error | Two handlers declare the same `[Operation]` id |
| `HRDOA005` | Warning | Two types publish under one schema name |
| `HRDOA018` | Error | `<HardenedOpenApiOutput>` is set and the assembly serves no document |
| `HRDOA019` | Error | The assembly serves more than one document |
| `HRDOA028` | Error | The file's extension is not `.json`, `.yaml` or `.yml` |
| `HRDOA029` | Error | `<HardenedOpenApiOutputVersion>` is not `3.0.0` or `3.1.0` |
| `HRDOA030` | Warning | A streaming operation lost its `itemSchema` in the lowered file |

[Diagnostics](/reference/diagnostics) lists every code.

## Limits

::: warning
A member whose name starts with more than one capital letter publishes with only the first letter
lowered: `ID` as `iD` and `URLPath` as `uRLPath`. The JSON body writes `id` and `urlPath`. The
Kiota client generated from the document reads nothing for such a member. Give it
`[JsonPropertyName]`.
:::

`[property: JsonPropertyName("id")]` on such a member publishes the name the body uses:

```csharp
using System.Text.Json.Serialization;

namespace Todos;

public record Link(
    [property: JsonPropertyName("id")] int ID,
    [property: JsonPropertyName("urlPath")] string URLPath
);
```

The document and the service also disagree in these cases:

| Case | The document | The service |
|---|---|---|
| An operation whose only input is a JSON body with no constraint | Lists no 400 | Answers 400 to an empty or malformed body |
| A GET handler that returns null, or returns nothing | Lists no 404 unless the handler declares one | Answers 404 |
| `[AuthorizeGrants]` without `[Authorize<TScheme>]` | Lists a 403 and no 401 | Answers 401 to a caller with no credentials |
| A filter attribute that declares its own 400 with `[AnswersStatus]` | Lists that 400 in place of the validation 400 | Answers `RequestValidationError` to a failed constraint on the operation |

[Routing](/guide/routing) covers what a null return answers.

A model declared in a referenced project publishes its members that have a property initializer as
required.

The template's `tests/Todos.Tests/DocumentStatusTests.cs` sends one request for each status the
template's routes answer. The test fails when the served document lists a status that no request
answered, or misses one that a request did.

## Next

- [Generated clients](/guide/clients): generating a client from the file
- [Declared responses](/guide/responses): the response types a handler declares
- [Generating from OpenAPI](/guide/openapi): a contract as the source of the document
- [Registered routes](/guide/registered-routes): routes registered at startup
- [The execution pipeline](/guide/execution-pipeline): `[AnswersStatus]` on a filter attribute of
  the application's own

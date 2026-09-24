# MessagePack

`Hardened.Requests.Serializers.MessagePack` writes and reads MessagePack beside JSON. An operation answers MessagePack when it declares `MessagePackContentType.Value` with `[Produces]` and the request's `Accept` asks for it.

`[MessagePackSerializerLibrary]` on a module registers the writer and the reader. The library module in `src/Todos/TodosLibrary.cs` carries it. The module is the one that `--serializer message-pack-keyed` scaffolds, without the `[JsonErrorBodies]` that the template also puts on it. [Error and built-in response bodies](#error-and-built-in-response-bodies) covers that attribute.

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Serializers.MessagePack;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization.Metadata;

namespace Todos;

[HardenedModule]
[HardenedWebModule]
[MessagePackSerializerLibrary]
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

MessagePack writes a model through a formatter. MessagePack's source generator writes the formatter for a type with `[MessagePackObject]`, such as `Todo` in `src/Todos/TodoStore.cs`:

```csharp
using MessagePack;

namespace Todos;

[MessagePackObject]
public partial record Todo(
    [property: Key(0)] int Id,
    [property: Key(1)] string Title,
    [property: Key(2)] bool Done
);
```

The operation in `src/Todos/TodoController.cs` declares both media types:

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Serializers.MessagePack;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

public class TodoController
{
    [Operation("getTodo")]
    [Get("/{id}")]
    [Produces(KnownContentType.Json, MessagePackContentType.Value)]
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

The operation answers these two requests:

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json

{"id":1,"title":"Read the generated code","done":true}
```

```http
GET /todos/1
Accept: application/x-msgpack

HTTP/1.1 200 OK
Content-Type: application/x-msgpack
```

The second response's body is these bytes, in hex:

```text
93 01 b7 52 65 61 64 20 74 68 65 20 67 65 6e 65 72 61 74 65 64 20 63 6f 64 65 c3
```

The body decodes to the array `[1, "Read the generated code", true]`.

## Adding the package

Run this command in the solution directory:

```bash
dotnet add src/Todos package Hardened.Requests.Serializers.MessagePack --version 0.0.0-HARDENED-VERSION
```

The package depends on `MessagePack` 3.1.8, which brings MessagePack's source generator into the project.

The template's solution uses central package management. There, the command adds a `PackageReference` to `src/Todos/Todos.csproj` and a `PackageVersion` to `Directory.Packages.props`. The `PackageVersion` holds the version as a literal. The template's other Hardened packages use `$(HardenedVersion)`.

`dotnet new hardened-web --serializer message-pack-keyed` or `message-pack-named` adds the package, the module attribute and the declarations. [Project templates](/guide/project-templates) covers the option.

## Declaring MessagePack on an operation

`MessagePackContentType` is in the namespace `Hardened.Requests.Serializers.MessagePack`. Its `Value` is `application/x-msgpack`. The package changes no response by itself, because the MessagePack writer is not a default writer. The declaration and the request's `Accept` decide the response:

| Operation | `Accept` | Response |
|---|---|---|
| No media type declared | `application/x-msgpack` | JSON |
| `[Produces(KnownContentType.Json, MessagePackContentType.Value)]` | None | JSON |
| `[Produces(KnownContentType.Json, MessagePackContentType.Value)]` | `application/x-msgpack` | MessagePack |
| `[Produces(KnownContentType.Json, MessagePackContentType.Value)]` | `application/msgpack` or `application/vnd.msgpack` | 406 |
| `[Produces(MessagePackContentType.Value)]` | None | MessagePack |
| `[Produces(MessagePackContentType.Value)]` | `application/json` | 406 |

`application/x-msgpack` is the only spelling. A request for `application/msgpack` gets a 406:

```http
GET /todos/1
Accept: application/msgpack

HTTP/1.1 406 Not Acceptable
Content-Type: application/json

{"type":"NotAcceptable","message":"This operation produces application/json, application/x-msgpack.","details":"application/json, application/x-msgpack"}
```

The package tells the build that it writes `application/x-msgpack`, so the build does not warn `HRDR012` when an operation declares it. [Content negotiation](/guide/content-negotiation) covers `[Produces]`, how `Accept` is matched and the 406.

## Request bodies

A body whose `Content-Type` contains `application/x-msgpack` is read as MessagePack, whatever the operation declares. Any other body is read as JSON. A request body model needs a MessagePack formatter too. The template's `NewTodo` carries `[MessagePackObject]`.

This request creates a todo from a MessagePack body:

```http
POST /todos
Content-Type: application/x-msgpack
Accept: application/x-msgpack

HTTP/1.1 201 Created
Content-Type: application/x-msgpack
Location: /todos/3
```

The first line is the request body and the second is the response body, in hex:

```text
91 a8 42 75 79 20 6d 69 6c 6b
93 03 a8 42 75 79 20 6d 69 6c 6b c2
```

The request body is the array `["Buy milk"]`, a keyed `NewTodo`. The response decodes to `[3, "Buy milk", false]`.

[Content negotiation](/guide/content-negotiation) covers how the deserializer is chosen.

## Keying a model

A model's MessagePack attributes decide its shape on the wire and what the OpenAPI document adds for it:

| Model | On the wire | In the OpenAPI document |
|---|---|---|
| `[MessagePackObject]`, `[Key(n)]` on each member | An array, in index order | `"x-message-pack-index": n` on each property |
| `[MessagePackObject(true)]`, `[Key("id")]` on each member | A map keyed `id`, `title`, `done` | Nothing added |
| `[MessagePackObject(true)]` with no `[Key]` | A map keyed `Id`, `Title`, `Done` | Nothing added |
| No `[MessagePackObject]` | 500, with an empty body | Nothing added |

The index in `[Key(n)]` is the key on the wire, so changing it breaks a client built before the change. Renaming the member does not.

Under `[MessagePackObject(true)]`, each member's key is its C# name unless `[Key("name")]` gives another. The OpenAPI document publishes the JSON name, so pin the key to it. The template declares `Todo` this way for `--serializer message-pack-named`:

```csharp
using MessagePack;

namespace Todos;

[MessagePackObject(true)]
public partial record Todo(
    [property: Key("id")] int Id,
    [property: Key("title")] string Title,
    [property: Key("done")] bool Done
);
```

With this model, a request for MessagePack gets a map:

```http
GET /todos/1
Accept: application/x-msgpack

HTTP/1.1 200 OK
Content-Type: application/x-msgpack
```

The body is these bytes, in hex:

```text
83 a2 69 64 01 a5 74 69 74 6c 65 b7 52 65 61 64 20 74 68 65 20 67 65 6e 65 72 61 74 65 64 20 63 6f 64 65 a4 64 6f 6e 65 c3
```

It decodes to the map `{"id": 1, "title": "Read the generated code", "done": true}`.

MessagePack's source generator also uses an `IMessagePackFormatter<T>` declared in the project, with no registration.

::: warning
The package has no fallback for a model with no MessagePack formatter. The build reports nothing. JSON responses are unaffected. A request for MessagePack gets a 500 with an empty body. In a contract, no generated model has a formatter when the build property `HardenedSerializer` is not set.
:::

## Keys in the OpenAPI document

A member with `[Key(n)]` publishes `"x-message-pack-index": n` on its property's schema. Hardened adds no index of its own. `[Key("name")]` publishes nothing. The OpenAPI document describes the keyed `Todo` this way:

```json
"Todo": {
  "type": "object",
  "description": "A todo, as it goes over the wire.",
  "required": [
    "id",
    "title",
    "done"
  ],
  "properties": {
    "id": {
      "type": "integer",
      "format": "int32",
      "x-message-pack-index": 0
    },
    "title": {
      "type": "string",
      "x-message-pack-index": 1
    },
    "done": {
      "type": "boolean",
      "x-message-pack-index": 2
    }
  }
}
```

[The OpenAPI document](/guide/openapi-document) covers the rest of the schema.

## Keys in a contract

In an OpenAPI-first project, the build property `HardenedSerializer` decides which MessagePack attributes the generated models carry. The value is compared without case. An absent or unknown value is `Json`.

| `HardenedSerializer` | Each generated model carries | Indexes in the contract |
|---|---|---|
| `Json`, the default | No MessagePack attributes | Not required |
| `MessagePackNamed` | `[MessagePackObject(true)]`, and `[Key("name")]` with the contract's property name | Not required |
| `MessagePackKeyed` | `[MessagePackObject]`, and `[Key(n)]` from `x-message-pack-index` | Required on every property |

`--serializer` sets the property in a scaffolded project. [Project templates](/guide/project-templates) lists its values. A project made with `--serializer message-pack-keyed --contract openapi` carries it in `src/Todos/Todos.csproj`:

```xml
<PropertyGroup>
  <HardenedSerializer>MessagePackKeyed</HardenedSerializer>
</PropertyGroup>
```

A response declares MessagePack with an `application/x-msgpack` entry under `content:`. The contract in `src/Todos/contracts/todos.yaml` declares both media types for `getTodo`:

```yaml
paths:
  /todos/{id}:
    get:
      operationId: getTodo
      responses:
        '200':
          description: The todo.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Todo'
            application/x-msgpack:
              schema:
                $ref: '#/components/schemas/Todo'
```

A property states its index with `x-message-pack-index`. A negative index is read as absent. The contract's `Todo` schema gives each property an index:

```yaml
components:
  schemas:
    Todo:
      type: object
      required:
        - id
        - title
        - done
      properties:
        id:
          type: integer
          format: int32
          x-message-pack-index: 0
        title:
          type: string
          x-message-pack-index: 1
        done:
          type: boolean
          x-message-pack-index: 2
```

The served document keeps each contract index.

Under `MessagePackKeyed`, a property with no index fails the build with `HOAT033`. The error names the next free index. This is the error with `x-message-pack-index` removed from `done`:

```text
src/Todos/contracts/todos.yaml : error HOAT033: Property 'done' of schema 'Todo' declares no x-message-pack-index, and $(HardenedSerializer) is MessagePackKeyed. Nothing assigns one, because an index this build chose would move when a property is added above it. Write "x-message-pack-index: 2" on the property.
```

Two properties at one index fail with `HOAT033` too. This is the error with `title` given index 0:

```text
src/Todos/contracts/todos.yaml : error HOAT033: Schema 'Todo' keys both 'id' and 'title' to x-message-pack-index 0. One index identifies one member; give one of them another.
```

## `oneOf` schemas

A `oneOf` schema gets no MessagePack formatter. Under `MessagePackNamed` or `MessagePackKeyed`, the build warns `HOAT034`. This is the warning for a `Payload` schema that is a `oneOf` of `Todo` and `NewTodo`, answered by `getPayload`:

```text
src/Todos/contracts/todos.yaml : warning HOAT034: Schema 'Payload' is a oneOf, and MessagePack does not carry one - a choice is resolved from a discriminator in the payload, which is a JSON-only shape here. It is generated and serialized as JSON as before; an operation that answers it as application/x-msgpack fails at the response. Declare that operation as JSON only.
```

The operation still answers the `oneOf` as JSON. A request for MessagePack gets a 500 with an empty body. Declare an operation that answers a `oneOf` as JSON only.

## Error and built-in response bodies

An operation that declares MessagePack sends a failure as MessagePack to a request for it: the 404 the handler returns, a validation or bind 400, a 409 and a 500. This is the 404 from `getTodo`, on a module without `[JsonErrorBodies]`:

```http
GET /todos/99
Accept: application/x-msgpack

HTTP/1.1 404 Not Found
Content-Type: application/x-msgpack
```

The body decodes to the map `{"resource": "todo", "detail": "No todo has id 99.", "type": "urn:hardened:problem:not-found", "title": "Not Found", "status": 404}`.

Hardened's own response bodies go out this way:

| Response | MessagePack body |
|---|---|
| The error envelopes, `ErrorModel` and `RequestValidationError` | A map |
| A built-in response record that carries a body, such as `NotFound` or `Conflict` | A map |
| A generic response record, such as `Created<T>` | Its payload, written by the payload's own formatter |
| A response record with no body, such as `NoContent` | Nothing |

`HardenedFormatterResolver` writes the maps. Each map has the keys and the order of its JSON body, under `MessagePackKeyed` too.

`[JsonErrorBodies]` on the module answers every failure as JSON instead. [Content negotiation](/guide/content-negotiation) covers it. `--serializer message-pack-named` and `message-pack-keyed` put `[JsonErrorBodies]` on the scaffold's library module.

A .NET client that reads one of these bodies composes `HardenedFormatterResolver.Instance` into its options. This `Program.cs` is from a console project that references `Hardened.Requests.Serializers.MessagePack`:

```csharp
using Hardened.Requests.Serializers.MessagePack;
using Hardened.Web.Runtime.Responses;
using MessagePack;
using MessagePack.Resolvers;

var options = MessagePackSerializerOptions.Standard.WithResolver(
    CompositeResolver.Create([], [HardenedFormatterResolver.Instance, StandardResolver.Instance])
);

using var http = new HttpClient();

using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost:5080/todos/99");

request.Headers.Accept.ParseAdd("application/x-msgpack");

using var response = await http.SendAsync(request);

var notFound = MessagePackSerializer.Deserialize<NotFound>(
    await response.Content.ReadAsByteArrayAsync(),
    options
);

Console.WriteLine($"{(int)response.StatusCode} {notFound.Title}: {notFound.Detail}");
```

The program prints `404 Not Found: No todo has id 99.`

## Adding a resolver

An `IFormatterResolver` registered in the container is asked before the package's own resolvers, for every type. `NativeGuidResolver` from MessagePack writes a `Guid` as 16 bytes rather than as a 36-character string. This `ConfigureServices` in `src/Todos/TodosLibrary.cs` registers it. The `using` lines at the top are the ones the change adds to the file:

```csharp
using MessagePack;
using MessagePack.Resolvers;

public void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);

    services.AddSingleton<IFormatterResolver>(NativeGuidResolver.Instance);
}
```

## Changing the options

`MessagePackSerializerConfiguration` is a configuration model in the namespace `Hardened.Requests.Serializers.MessagePack`. Amend it the way any configuration model is amended. [Configuration](/guide/configuration) covers `AppConfig`.

The model's `OptionsProvider` is a function from the service provider to the `MessagePackSerializerOptions` that the writer and the reader use. The function returns `MessagePackSerializerOptions.Standard` by default.

The package replaces the resolver of the options that the function returns with its own. A resolver chosen there is not used. Register a resolver in the container instead.

This `ConfigureServices` in `src/Todos/TodosLibrary.cs` amends the configuration model. The `using` lines at the top are the ones the change adds to the file:

```csharp
using Hardened.Shared.Runtime.Configuration;
using MessagePack;

public void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);

    var config = new AppConfig();

    config.Amend(
        (MessagePackSerializerConfiguration messagePack) =>
            messagePack.OptionsProvider = _ =>
                MessagePackSerializerOptions.Standard.WithCompression(
                    MessagePackCompression.Lz4BlockArray
                )
    );

    services.AddSingleton<IConfigurationPackage>(config);
}
```

With these options, a response body goes out compressed with LZ4. A short body goes out uncompressed. A client must read LZ4 to read a compressed body.

## The reference page

`DecodeMessagePack = true` on `[HardenedOpenApiUi]` makes the reference page decode MessagePack response bodies in its request panel and show them as JSON. `src/Todos.Host/Application.cs` sets it:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Runtime.OpenApi;

namespace Todos.Host;

[HardenedModule]
[KestrelRuntime]
[HardenedOpenApiUi(Title = "Todos", Environments = "development", DecodeMessagePack = true)]
[TodosLibrary]
public partial class Application;
```

The page decodes bodies of `application/x-msgpack` and `application/msgpack`. JSON bodies keep the page's own rendering. A keyed body is shown with the document's property names, read from `x-message-pack-index`. An array that the indexes do not fit is shown as it arrived. The indexes do not fit when a property has no index or the array is too short.

The page loads the decoder from `MessagePackScriptUrl`. By default, that is `@msgpack/msgpack` 3.1.3 on jsDelivr. [The OpenAPI document](/guide/openapi-document) covers the page's other properties, and what the setting changes about the page's scripts.

## A Refit client

With `--client refit`, the template's client project carries the MessagePack attributes on its models. [Generated clients](/guide/clients) covers what the template adds.

A Refit client sends and reads JSON until `RefitSettings.ContentSerializer` is set. The template writes `MessagePackContentSerializer` into the client project for it. The serializer reads a body that starts with `{` or `[` as JSON, and any other body as MessagePack.

The generated interface puts the document's media types on each call as headers. The call with a body carries `[Headers("Accept: application/json, application/x-msgpack", "Content-Type: application/json")]`. A header on the call wins over `HttpClient.DefaultRequestHeaders`. To send and receive MessagePack, replace both headers on each request with a `DelegatingHandler`. The template's `MessagePackClientFactory`, in the test project, does this.

Refit reads an error response's body as a string, so it cannot read a MessagePack error body. Declare `[JsonErrorBodies]`, which the template does.

## Limits

A request body that cannot be read gets one of these responses:

| Request body | Response |
|---|---|
| MessagePack that does not decode | 500, with a `ServerError` body |
| An empty MessagePack body | 500, with a `ServerError` body |
| A MessagePack map, where the model is keyed by index | 500, with a `ServerError` body |
| JSON that does not parse | 400 |

This request sends the one-byte body `c1`:

```http
POST /todos
Content-Type: application/x-msgpack

HTTP/1.1 500 Internal Server Error
Content-Type: application/json

{"type":"ServerError","message":"The server could not complete this request.","details":""}
```

The OpenAPI document lists a request body under `application/json` alone, including when a contract lists `application/x-msgpack` for it.

## Next

| Page | Covers |
|---|---|
| [Content negotiation](/guide/content-negotiation) | `[Produces]`, `Accept` and `[JsonErrorBodies]` |
| [Generated clients](/guide/clients) | The Refit client project and its templates |
| [Generating from OpenAPI](/guide/openapi) | A contract as the source of the models |
| [Project templates](/guide/project-templates) | The `--serializer` option |

# Generating from Smithy

Point the build at a [Smithy](https://smithy.io) model and it writes the models, a service
interface, the routes and the request validation: the same output an
[OpenAPI document](/guide/openapi) produces, from the same generator. Smithy is the contract
language, and the C# you write against it is identical.

```csharp
public partial interface ITodosService {
    /// <summary>GET /todos/{id} → 200</summary>
    Task<Todo?> GetTodo(int id);

    /// <summary>POST /todos → 201</summary>
    Task<Todo> CreateTodo(NewTodo body);

    /// <summary>DELETE /todos/{id} → 204</summary>
    Task RemoveTodo(int id);
}
```

```csharp
using Hardened.Requests.Abstract.Attributes;
using Todos.Models;
using Todos.Services;

[Handler]
public class TodoService(ITodoStore store) : ITodosService {

    public Task<Todo?> GetTodo(int id) => store.Find(id);

    public async Task<Todo> CreateTodo(NewTodo body) {
        if (await store.TitleExists(body.Title)) {
            throw new TodoTitleTaken($"A todo titled '{body.Title}' already exists.").AsException();
        }

        return await store.Add(body.Title);
    }

    public async Task RemoveTodo(int id) {
        if (!await store.Remove(id)) {
            throw new TodoNotFound($"No todo has id {id}.").AsException();
        }
    }
}
```

There are no route attributes and nothing to register.

## The model

Reference the generator and declare the model:

```xml
<ItemGroup>
    <PackageReference Include="Hardened.Smithy.SourceGenerator" PrivateAssets="all" />
</ItemGroup>

<ItemGroup>
    <HardenedSmithyModel Include="contracts\todos.smithy">
        <PublishUrl>/openapi.json</PublishUrl>
        <UiUrl>/docs</UiUrl>
    </HardenedSmithyModel>
</ItemGroup>
```

The application module needs the web module and nothing else:

```csharp
[HardenedModule]
[HardenedWebModule]
public partial class Application { }
```

::: warning The build runs the Smithy CLI
`HardenedSmithyModel` compiles `.smithy` sources into a JSON AST, which needs the
[Smithy CLI](https://smithy.io/2.0/guides/smithy-cli/index.html) on `PATH`. Two CLI versions can
produce different ASTs and therefore different C#, so the version is pinned:

```xml
<HardenedSmithyCliVersion>1.73.0</HardenedSmithyCliVersion>
```

A mismatch fails with `HSMT011` naming both versions on a CI build, and warns on a local one. Set
`<HardenedSmithyPinCliVersion>true</HardenedSmithyPinCliVersion>` to pin locally too. To build
with no CLI at all, see [Committing the AST](#committing-the-ast).
:::

The interface above came from this model:

::: details contracts/todos.smithy
```smithy
$version: "2"

namespace com.example.todos

@title("Todo API")
service Todos {
    version: "2024-01-01"
    operations: [GetTodo, CreateTodo, RemoveTodo]
}

structure Todo {
    @required
    id: Integer

    @required
    title: String

    @required
    done: Boolean
}

structure NewTodo {
    @required
    @length(min: 1, max: 64)
    title: String
}

@error("client")
@httpError(404)
structure TodoNotFound {
    @required
    message: String
}

@error("client")
@httpError(409)
structure TodoTitleTaken {
    @required
    message: String
}

@documentation("One todo by id.")
@http(method: "GET", uri: "/todos/{id}", code: 200)
@readonly
operation GetTodo {
    input := {
        @httpLabel
        @required
        @range(min: 1)
        id: Integer
    }

    output: Todo

    errors: [TodoNotFound]
}

@documentation("Creates a todo.")
@http(method: "POST", uri: "/todos", code: 201)
operation CreateTodo {
    input: NewTodo

    output: Todo

    errors: [TodoTitleTaken]
}

@documentation("Removes a todo.")
@http(method: "DELETE", uri: "/todos/{id}", code: 204)
@idempotent
operation RemoveTodo {
    input := {
        @httpLabel
        @required
        @range(min: 1)
        id: Integer
    }

    errors: [TodoNotFound]
}
```
:::

## What the model decides

Three things in the interface came from the model rather than from a choice:

- `GetTodo` returns `Todo?` because the operation declares `TodoNotFound`. Returning `null`
  answers 404 with that shape.
- `RemoveTodo` returns bare `Task` because its `@http` code is 204 and it declares no output.
- `id` is `int` rather than `string` because `@httpLabel` binds a member whose type the model
  states.

The interface is one per service shape, in `<RootNamespace>.Services`, named for the service. The
models are positional records in `<RootNamespace>.Models`, carrying each shape's constraints as
validation attributes:

```csharp
public partial record Todo(int Id, string Title, bool Done);

public partial record NewTodo(
    [property: Required] [property: StringLength(Min = 1, Max = 64)] string Title);

public partial record TodoNotFound([property: Required] string Message);

public partial record TodoTitleTaken([property: Required] string Message);
```

A Smithy error is a named shape, so each one also produces an exception type named for the
shape, shared by every operation that binds it:

```csharp
public partial class TodoNotFoundException : StatusCodeException { }
public partial class TodoTitleTakenException : StatusCodeException { }
```

You rarely name it. `AsException()` is generated on the error's body, so the throw is written
once: `throw new TodoNotFound($"No todo has id {id}.").AsException()`.

The build also emits a handler per operation, the routing table, and a validation filter that
enforces `@required`, `@length`, `@range` and `@pattern` before your code runs.

## HTTP bindings

Members bind from the traits the model puts on them, and the generated signature follows:

| Trait | Binds from | Generated parameter |
|---|---|---|
| `@httpLabel` | a path segment named by `uri` | positional, typed from the member |
| `@httpQuery("name")` | the query string | positional, nullable when not `@required` |
| `@httpHeader("X-Name")` | a request header | positional, named from the wire spelling |
| none | the request body | one `body` parameter of the input shape |

An output member carrying `@httpHeader` leaves as a header rather than in the JSON, and stays an
ordinary member of the returned record:

```smithy
operation CreatePet {
    output := {
        @required
        pet: Pet

        @httpHeader("Location")
        location: String
    }
}
```

```csharp
public async Task<CreatePetOutput> CreatePet(CreatePetInput body) {
    var created = await pets.Add(body);

    return new CreatePetOutput(created, "/pets/" + created.Id);
}
```

## Shapes

| Smithy | C# |
|---|---|
| `structure` | a positional `record` |
| `list` | `List<T>` |
| `map` | `Dictionary<string, T>` |
| `enum` | a C# `enum`, with the declared string values as its wire vocabulary |
| `union` | a struct with one implicit conversion per member and an `object? Value` |
| `@streaming union`, bound as an `@httpPayload` output | the same struct, and `IAsyncEnumerable<TUnion>` on the interface. The response streams as server-sent events: each item is one member as `data:`, with the member's name as `event:`, and the document describes the item as the choice of its members |
| `document` | `JsonElement` |
| `Timestamp` | `DateTimeOffset` |
| `Blob` | `byte[]` |
| `@jsonName("x")` | `[JsonPropertyName("x")]` on the property |

## Bounding an operation

`@timeout` is Hardened's own trait, defined in `hardened.smithy`, which the build adds to your
model:

```smithy
use hardened.api#timeout

@http(method: "GET", uri: "/pets/{petId}")
@readonly
@timeout(milliseconds: 2000)
operation GetPet { }
```

| Member | |
|---|---|
| `milliseconds` | required, and greater than zero |
| `status` | what the caller is told, 504 unless stated |
| `retryAfterSeconds` | for a `status` of 503 |

A budget stated here is the operation's own, and the nearest declaration wins: a
[`[Timeout]`](/guide/request-timeouts) on the generated implementation's method or class
overrides it, and it overrides the assembly's and the application's default.

## Authentication

`@httpBearerAuth` on the service requires every operation to authenticate. `@auth([])` on an
operation opts it back out:

```smithy
@httpBearerAuth
service PetStore {
    operations: [GetPet, GetSecuredPet]
}

@auth([])
@http(method: "GET", uri: "/pets/{petId}", code: 200)
operation GetPet { ... }
```

Smithy has no scopes, so a model can require an authenticated caller and never a particular
grant. To require grants, put [`[AuthorizeGrants]`](/guide/authorization) on the implementation.
A contract can narrow what is admitted and never widen it.

## Declaring the whole response set

Set `HardenedResponseModel` and each operation's declared errors become cases on a generated
response container, which the compiler checks you handled:

```xml
<PropertyGroup>
    <HardenedResponseModel>Response</HardenedResponseModel>
</PropertyGroup>
```

```csharp
public async Task<GetTodoResponse> GetTodo(int id) {
    var todo = await store.Find(id);

    if (todo is null) {
        return new NotFound("todo", $"No todo has id {id}.");
    }

    return todo;
}
```

The error case is named for the shape, so `GetTodo` and `RemoveTodo` share `TodoNotFoundError`
rather than getting one case each. A success case is still named for the operation, since it
carries the operation's own payload.

The handler returns the framework's bare `NotFound`, and the container converts it into
`TodoNotFoundError` with the detail as the shape's `message`. The build writes that conversion for
an `@error` shape whose required members it can fill: `message`, and the RFC 7807 members when the
shape declares `title` and `status`. A shape requiring anything else is constructed by hand, as
`new TodoNotFoundError(new TodoNotFound(...))`.

`Throws`, `Response` or `Union`; absent means `Throws`. See
[Declared responses](/guide/responses).

## Serving the document

`PublishUrl` serves the OpenAPI document generated from the model, not the Smithy AST, so the
usual clients and the reference page at `UiUrl` can read it. `UiEnvironments` limits which
[environments](/guide/environments) serve the page:

```xml
<HardenedSmithyModel Include="contracts\todos.smithy">
    <PublishUrl>/openapi.json</PublishUrl>
    <UiUrl>/docs</UiUrl>
    <UiEnvironments>development</UiEnvironments>
</HardenedSmithyModel>
```

There is no `SourceUrl`. A Smithy model is not a document an OpenAPI client can read.

## Committing the AST

`HardenedSmithyAst` takes a JSON AST directly, which resolves no tool and builds on a machine with
no Smithy CLI:

```bash
smithy ast --flatten contracts/todos.smithy > contracts/todos.json
```

```xml
<ItemGroup>
    <HardenedSmithyAst Include="contracts\todos.json" />
</ItemGroup>
```

Everything downstream is identical, and a project can use both inputs together. Pointing
`HardenedSmithyAst` at a `.smithy` file is `HSMT003`.

## Build properties

| Property | Effect |
|---|---|
| `HardenedSmithyModelName` | Names the generated AST and the generated source. Defaults to the project name |
| `HardenedSmithyNamespace` | Root for the generated types. Defaults to `RootNamespace` |
| `HardenedSmithyServiceShapeId` | Selects one service when the model declares several |
| `HardenedSmithyCliVersion` | The CLI version this build expects. Defaults to `1.73.0` |
| `HardenedSmithyPinCliVersion` | Whether a version mismatch fails or warns. Defaults to `ContinuousIntegrationBuild` |
| `HardenedResponseModel` | `Throws`, `Response` or `Union` |
| `ExcludeGeneratedCodeFromCoverage` | `[ExcludeFromCodeCoverage]` on generated types. Defaults to `true` |

All `HardenedSmithyModel` items in a project form one model in one CLI invocation, since a Smithy
service is routinely written across several files. A project needing two independent services
generates their ASTs separately and points `HardenedSmithyAst` at the results.

::: warning Import the targets below the item group
An in-repo `<Import>` of `Hardened.Smithy.SourceGenerator.targets` has to come after the
`HardenedSmithyAst` or `HardenedSmithyModel` item group, or no generated source reaches the
compilation. The build reports `HSMT005` and names the fix. A `PackageReference` imports the
targets for you and is unaffected.
:::

## Starting from a template

```bash
dotnet new hardened-web -n Todos --contract smithy
```

That writes the model, the wiring, the implementation and tests. See
[Project templates](/guide/project-templates).

## Testing

Generated routes are ordinary Hardened routes, so `ITestWebApp` and a generated client drive them
through the same pipeline. See [Sending requests](/guide/testing-web) and
[Typed clients](/guide/testing-clients).

## Next

- [Generating from OpenAPI](/guide/openapi): the same generated output from an OpenAPI document
- [Declared responses](/guide/responses): the three response models in full
- [The OpenAPI document](/guide/openapi-document): serving a document and a reference page

# Generating from Smithy

A `HardenedSmithyModel` item in the project file names a Smithy model. The build runs the Smithy CLI
over the model and generates the same output that an OpenAPI document gives: the models, a service
interface, a handler for each operation, the routing table and the request validation.

The project file, `src/Todos/Todos.csproj`, references the Smithy generator and names the model:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <HardenedResponseModel>Response</HardenedResponseModel>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Hardened.Shared.Runtime" />
    <PackageReference Include="Hardened.Web.Runtime" />
    <PackageReference Include="Hardened.Library.SourceGenerator" />
    <PackageReference Include="ValidationModules.SourceGenerator" />
    <PackageReference Include="Hardened.Smithy.SourceGenerator" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <HardenedSmithyModel Include="contracts\todos.smithy" />
  </ItemGroup>
</Project>
```

The model, `src/Todos/contracts/todos.smithy`, declares one service with one operation:

```smithy
$version: "2"

namespace com.example.todos

@title("Todos API")
service Todos {
    version: "2024-01-01"
    operations: [GetTodo]
}

structure Todo {
    @required
    id: Integer

    @required
    title: String

    @required
    done: Boolean
}

@error("client")
@httpError(404)
structure TodoNotFound {
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
```

A class that carries `[Handler]` and implements the generated interface, `ITodosService`, answers the
model's operations. The class declares no route attributes, and the application registers nothing.
The implementation is the same C# as for an OpenAPI document with the same operation.
`src/Todos/TodoService.cs` uses the template's store, `ITodoStore`:

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Web.Runtime.Responses;
using Todos.Models;
using Todos.Services;

namespace Todos;

[Handler]
public class TodoService(ITodoStore store) : ITodosService
{
    public async Task<GetTodoResponse> GetTodo(int id)
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

{"id":1,"title":"Read the generated code","done":true}
```

```http
GET /todos/99

HTTP/1.1 404 Not Found
Content-Type: application/json

{"message":"No todo has id 99."}
```

The 404 carries the `TodoNotFound` shape. The build fills its `message` from the detail of the
`NotFound` that the handler returns. [Errors](#errors) has the rule.

## Declaring the model

The project references `Hardened.Smithy.SourceGenerator` with `PrivateAssets="all"`, beside the same
runtime and generator packages that an OpenAPI project uses. The rules on
`Hardened.Web.SourceGenerator`, `PrivateAssets` and the package versions are the same as for an
OpenAPI project. [Generating from OpenAPI](/guide/openapi) states them. The module class needs
`[HardenedModule]` and `[HardenedWebModule]`, as for an OpenAPI document.
`dotnet new hardened-web --contract smithy` writes this project.
[Project templates](/guide/project-templates) covers the option.

The build runs the Smithy CLI, `smithy`, from `PATH`, or from the path that `HardenedSmithyCliPath`
names. [Smithy's documentation](https://smithy.io/2.0/guides/smithy-cli/cli_installation.html)
covers installing the CLI. Without the CLI, the build stops with `HSMT010`.

The build is pinned to the CLI version that `HardenedSmithyCliVersion` names, `1.73.0` unless the
project sets another. A CLI of another version gives warning `HSMT011`, which names both versions.
The build continues. With `HardenedSmithyPinCliVersion` set to `true`, the mismatch is error
`HSMT011`. The property defaults to `ContinuousIntegrationBuild`, so a CI build fails on a mismatch.

A model that the CLI refuses stops the build with `HSMT012`. The build reports one error for each
finding, at the file and line that the CLI names.

One CLI run compiles every `HardenedSmithyModel` item in a project as one model. The build adds
Hardened's own trait definitions, `hardened.smithy`, to the model.
[Traits from Hardened](#traits-from-hardened) lists them. The CLI runs when a `.smithy` file changes.

## A committed AST

A `HardenedSmithyAst` item names a Smithy JSON AST in place of `.smithy` sources. The build then runs
no CLI. `smithy ast --flatten` writes the AST to standard output:

```bash
smithy ast --flatten contracts/todos.smithy > contracts/todos.json
```

When the model uses `@timeout`, `@validation` or `@narrowed`, the command also takes `hardened.smithy`. Without it,
the CLI refuses the model with `Model.UnresolvedTrait`. The `Hardened.Smithy.SourceGenerator` package
holds the file in its `build` folder. The package is in the NuGet packages folder, `~/.nuget/packages`
unless `NUGET_PACKAGES` names another:

```bash
smithy ast --flatten contracts/todos.smithy ~/.nuget/packages/hardened.smithy.sourcegenerator/0.0.0-HARDENED-VERSION/build/hardened.smithy > contracts/todos.json
```

The item names the AST file:

```xml
<ItemGroup>
  <HardenedSmithyAst Include="contracts\todos.json" />
</ItemGroup>
```

The generated file and the helper types are named after the AST file. `todos.json` gives
`todos.g.cs` and `TodosProblems`. A `.smithy` file named by `HardenedSmithyAst` stops the build with
`HSMT003`. A project can hold both item types.

## What the build generates

The generated types, their namespaces and `HardenedResponseModel` work as for an OpenAPI document.
[Generating from OpenAPI](/guide/openapi) covers the service interface, the three response models
and the attributes an implementation can carry.

The build generates one interface for each service shape, named `I<Service>Service`, such as
`ITodosService`. Operations that a service reaches through a `resource` go on the service's
interface. Each method is named for its operation shape. The served document's `operationId` is the
shape name, such as `GetTodo`. The methods are in the alphabetical order of the operation names.
`@documentation` on an operation is the method's doc comment and the operation's `description` in
the served document.

The CLI names an inline `input :=` or `output :=` structure `<Operation>Input` or
`<Operation>Output`. The generated record has that name, such as `SearchTodosOutput`. When some
input members bind to the path, the query or headers, the body is a record of the rest, named
`<Input>Body`, such as `CreateWidgetInputBody`.

A service's `rename` is not read. The generated names are the shapes' own names.

For a `HardenedSmithyModel` item, the helper types, such as `TodosProblems`, take their prefix from
`HardenedSmithyModelName`. It defaults to the project's name. `TodosProblems` holds the methods that
convert a framework record, such as `NotFound`, into an error case of the model.
[Generating from OpenAPI](/guide/openapi) covers the conversion.

## HTTP bindings

A trait on each input member decides where the member binds from and how its parameter is named:

| Trait on an input member | Binds from | C# parameter |
|---|---|---|
| `@httpLabel` | The path segment of the same name | Named for the member. Required |
| `@httpQuery("q")` | The query value `q` | Named from `q`. Nullable unless `@required` |
| `@httpHeader("X-Page-Size")` | The request header | Named from the header: `xPageSize`. Nullable unless `@required` |
| `@httpPayload` | The whole request body | `body` |
| None | A member of the JSON body | `body`, typed as the input structure or `<Input>Body` |

A query or header parameter is named from the name on the wire, not from the member. `text` bound
to `@httpQuery("q")` is `q`.

A `@mixin` structure's members, with their binding traits, become members of every structure that
uses it. The build reads the model with `smithy ast --flatten`, which copies them in. Here every
operation that uses `FleetScoped` binds the `X-Fleet-Id` header and the `vin` label:

```smithy
@mixin
structure FleetScoped {
    @required
    @httpHeader("X-Fleet-Id")
    fleetId: String

    @required
    @httpLabel
    vin: String
}

@readonly
@http(method: "GET", uri: "/vans/{vin}", code: 200)
operation GetVan {
    input := with [FleetScoped] {
        @httpQuery("verbose")
        verbose: Boolean
    }
}
```

An `@httpPayload` blob is the raw request body, under the blob's `@mediaType`, or
`application/octet-stream` without one. The handler receives the bytes as `byte[]`, and the served
document declares the body as `format: binary`.

`SearchTodos`, added to the service's `operations`, binds a query value and a header:

```smithy
list TodoList {
    member: Todo
}

@documentation("Todos whose title contains a text.")
@http(method: "GET", uri: "/todos/search", code: 200)
@readonly
operation SearchTodos {
    input := {
        @httpQuery("q")
        @required
        @length(min: 2)
        text: String

        @httpHeader("X-Page-Size")
        @range(min: 1, max: 50)
        pageSize: Integer
    }

    output := {
        @required
        todos: TodoList

        @httpHeader("X-Total-Count")
        total: String
    }
}
```

`@httpHeader` on an output member sends it as a response header and leaves it out of the JSON body.
The member stays on the output record. The handler sets it:

```csharp
public async Task<SearchTodosOutput> SearchTodos(string q, int? xPageSize)
{
    var matches = (await store.All())
        .Where(todo => todo.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
        .ToList();

    return new SearchTodosOutput(matches.Take(xPageSize ?? 10).ToList(), matches.Count.ToString());
}
```

```http
GET /todos/search?q=the
X-Page-Size: 5

HTTP/1.1 200 OK
Content-Type: application/json
X-Total-Count: 1

{"todos":[{"id":1,"title":"Read the generated code","done":true}]}
```

A constraint failure is named as the request names the value: `q`, `X-Page-Size`, or
`body.<member>`.

```http
GET /todos/search?q=a
X-Page-Size: 99

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"q","code":"string_length","message":"q must be at least 2 characters."},{"field":"X-Page-Size","code":"range","message":"X-Page-Size must be between 1 and 50."}]}
```

## A body that is not a structure

`@httpPayload` on an output member makes that member the whole response body. The response's media
type is the `@mediaType` on the payload's target shape. Without one, a `blob` is
`application/octet-stream` and anything else is `application/json`.

The payload's target decides the method's return type:

| Payload target | Return type |
|---|---|
| `string` | `Task<string>` |
| `blob` | `Task<byte[]>` |
| A `list` of `Todo` | `Task<List<Todo>>` |

A `string` payload is written as it is. A `list` payload is a JSON array. A `blob` inside a
structure is base64 in the JSON body.

`ExportTodos` returns a `string` shape that carries `@mediaType("text/csv")`:

```smithy
@mediaType("text/csv")
string TodoCsv

@documentation("Every todo as CSV.")
@http(method: "GET", uri: "/todos/export", code: 200)
@readonly
operation ExportTodos {
    output := {
        @required
        @httpPayload
        csv: TodoCsv
    }
}
```

The method returns the CSV text as a `string`:

```csharp
public async Task<string> ExportTodos()
{
    var csv = new StringBuilder("id,title,done\n");

    foreach (var todo in await store.All())
    {
        csv.Append($"{todo.Id},{todo.Title},{todo.Done}\n");
    }

    return csv.ToString();
}
```

```http
GET /todos/export

HTTP/1.1 200 OK
Content-Type: text/csv

id,title,done
1,Read the generated code,True
2,Add an endpoint,False
```

## Shapes and C# types

Each shape maps to a C# type:

| Smithy | C# |
|---|---|
| `structure` | A `sealed partial` record |
| `String` | `string` |
| `Boolean` | `bool` |
| `Byte`, `Short`, `Integer` | `int` |
| `Long` | `long` |
| `Float` | `float` |
| `Double` | `double` |
| `BigDecimal` | `decimal` |
| `BigInteger` | `long` |
| `Timestamp` | `DateTimeOffset` |
| `Blob` | `byte[]` |
| `Document` | `JsonElement` |
| `list` | `List<T>` |
| `map` | `Dictionary<string, T>` |
| `enum` | A C# `enum` |
| `intEnum` | `int` |
| `union` | A struct holding one member |
| A `@streaming` `union` bound with `@httpPayload` on an output | The struct, and the method returns `IAsyncEnumerable<T>` |
| A named simple shape, such as `string TodoCsv` | Its target's type. No C# type is generated |

`BigDecimal` and `BigInteger` give warning `HSMT006` naming the member, unless the member or its
shape carries [`@narrowed`](#traits-from-hardened).

A member without `@required` is nullable in C#. Its constructor parameter defaults to `default`. The
member is written as `null` when it has no value. A member with `@default` is left out instead.
`@clientOptional` on a `@required` member makes it nullable, with no default.

An `enum`'s C# members are its Smithy members in Pascal case. The JSON reads and writes the
`@enumValue`s. `DARK_BLUE = "dark-blue"` is `DarkBlue`, sent as `"dark-blue"`.
`@jsonName("display_name")` sets the member's name in the JSON.

A `union` struct has a constructor and an implicit conversion for each member. It also has
`object? Value`. On the wire the union is the member's value alone. The served document describes
it as a `oneOf`.

A streamed union is sent as server-sent events, with each item's member name as the `event:` field.
[Streaming responses](/guide/streaming) covers the framing.

## Errors

A structure with `@error` in an operation's `errors` is a declared error. So is one in the
service's `errors`, which applies to every operation in the service. Its status is its
`@httpError`. Without one, `@error("client")` is 400 and `@error("server")` is 500.

The build generates a type for every error, named for the shape. Under `Throws` it is
`<Shape>Exception`, such as `TodoNotFoundException`. Under `Response` and `Union` it is a case
`<Shape>Error(<Shape> Body)`, such as `TodoNotFoundError`. Two operations that declare one error
share its type.

Under `Response` and `Union`, the framework's record for the status, such as `NotFound`, converts
into the error's case when the build can fill every required member. `message` takes the record's
detail, or its title when the record has no detail. `title` and `status` are filled when the shape
declares them. A shape with another required member has no conversion. The handler returns the case
instead: `new TodoNotFoundError(new TodoNotFound(...))`.

The compiler does not check that a handler returns every error its operation declares. A handler
compiles unchanged after the model adds an error. The template's `DocumentStatusTests` is the check,
as [Generating from OpenAPI](/guide/openapi#response-and-union) describes.

Under `Throws`, the same record thrown with `AsException()` is sent as the error's shape, filled the
same way. An operation that declares two errors at one status fills neither, and the record is sent
as it was thrown.

A member of an error shape with `@httpHeader` is sent as that response header, and the served
document declares it under the error response's `headers`. The member stays on the shape's record
and is not in the JSON body:

```smithy
@error("client")
@httpError(429)
structure Throttled {
    message: String

    @httpHeader("Retry-After")
    retryAfter: String
}
```

`throw new Throttled("Slow down.", RetryAfter: "30").AsException()` answers 429 with
`Retry-After: 30` and the body `{"message":"Slow down."}`. Under an awsJson protocol, which
ignores HTTP binding traits, the member stays in the body.

Under `Throws`, the build generates `AsException()` on the error shape, in `<Model>Errors`. A
handler throws `new TodoTitleTaken(...).AsException()`. Under `Throws`, a `GET` or `PUT` operation
that declares an error at 404 returns a nullable payload. When the handler returns null, the
operation answers 404, with the shape's `message` set to `Not Found`.

With `--response-model throws`, the template's model declares `TodoTitleTaken` on `CreateTodo`:

```smithy
@error("client")
@httpError(409)
structure TodoTitleTaken {
    @required
    message: String
}

@documentation("Creates a todo.")
@http(method: "POST", uri: "/todos", code: 201)
operation CreateTodo {
    input: NewTodo

    output: Todo

    errors: [TodoTitleTaken]
}
```

The template's `src/Todos/TodoService.cs` throws the framework's records, and each is sent as the
declared error at its status:

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Web.Runtime.Responses;
using Todos.Models;
using Todos.Services;

namespace Todos;

[Handler]
public class TodoService(ITodoStore store) : ITodosService
{
    public async Task<List<Todo>> ListTodos() => (await store.All()).ToList();

    public Task<Todo?> GetTodo(int id) => store.Find(id);

    public async Task<Todo> CreateTodo(NewTodo body)
    {
        if (await store.TitleExists(body.Title))
        {
            throw new Conflict($"A todo titled '{body.Title}' already exists.").AsException();
        }

        return await store.Add(body.Title);
    }

    public async Task RemoveTodo(int id)
    {
        if (!await store.Remove(id))
        {
            throw new NotFound("todo", $"No todo has id {id}.").AsException();
        }
    }
}
```

```http
POST /todos
Content-Type: application/json

{"title":"Read the generated code"}

HTTP/1.1 409 Conflict
Content-Type: application/json

{"message":"A todo titled \u0027Read the generated code\u0027 already exists."}
```

```http
GET /todos/99

HTTP/1.1 404 Not Found
Content-Type: application/json

{"message":"Not Found"}
```

## Protocols

A protocol trait on the service decides how operations are reached:

| Protocol trait on the service | How operations are reached |
|---|---|
| None | By each operation's `@http` method and path |
| `aws.protocols#restJson1` | By each operation's `@http` method and path |
| `aws.protocols#awsJson1_0`, `aws.protocols#awsJson1_1` | Every operation is `POST /`, named by the header `X-Amz-Target: <Service>.<Operation>` |
| `aws.protocols#restXml`, `aws.protocols#awsQuery`, `aws.protocols#ec2Query` | Refused: the build stops with `HSMT002` |

The Smithy CLI does not carry the `aws.protocols` and `aws.auth` traits. A model that uses them needs
a `smithy-build.json` beside the project file. `src/Todos/smithy-build.json` names the
`smithy-aws-traits` package at the CLI's version:

```json
{
  "version": "1.0",
  "maven": {
    "dependencies": ["software.amazon.smithy:smithy-aws-traits:1.73.0"]
  }
}
```

The CLI downloads the package from Maven Central.

Under an awsJson protocol, the input structure is the whole body. The HTTP binding traits are
ignored, `@http` and `@httpError` included. An awsJson error body carries `__type`, the error's full
shape id: `{"message":"No thing 2.","__type":"com.example.rpc#ThingMissing"}`.

With no protocol trait or with `restJson1`, an operation that lacks `@http` is skipped with warning
`HSMT006`.

## Authentication

A service that carries `@httpBearerAuth`, `@httpBasicAuth`, `@httpApiKeyAuth`, `@httpDigestAuth` or
`@aws.auth#sigv4`, or a non-empty `@auth`, requires an authenticated caller on every operation.
`@auth([])` or `@optionalAuth` on an operation removes that requirement.

A Smithy model has no scopes, so it requires an authenticated caller and never a grant.
`[AuthorizeGrants]` on the implementation adds grants. [Authorization](/guide/authorization) covers
the attribute. The application's authentication decides who the caller is.
[Authentication](/guide/authentication) covers it.

The served document declares the service's scheme. Each operation that requires it gets a
`security` entry. `@aws.auth#sigv4` is enforced and not published.

In this model the service carries `@httpBearerAuth`, and `GetTodo` carries `@auth([])`:

```smithy
@title("Todos API")
@httpBearerAuth
service Todos {
    version: "2024-01-01"
    operations: [GetTodo, SearchTodos, ExportTodos]
}

@documentation("One todo by id.")
@auth([])
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
```

```http
GET /todos/search?q=the

HTTP/1.1 401 Unauthorized
Content-Type: application/json
WWW-Authenticate: Bearer

{"type":"AuthorizationException","message":"This request requires authentication.","details":""}
```

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json

{"id":1,"title":"Read the generated code","done":true}
```

## Traits from Hardened

`hardened.smithy` declares three traits:

| Trait | On | Effect |
|---|---|---|
| `hardened.api#timeout` | An operation | The operation's time budget: `milliseconds`, required, and `status` and `retryAfterSeconds` |
| `hardened.api#validation` | An operation | `"stop-on-first-error"` answers a request that fails validation with its first failure only. `"collect-all"` answers with every failure |
| `hardened.api#narrowed` | A member, or a simple shape | Stops warning `HSMT006` for a `BigDecimal` or `BigInteger` it covers |

A model names them with `use hardened.api#timeout`, `use hardened.api#validation` and
`use hardened.api#narrowed`:

```smithy
use hardened.api#timeout

@documentation("Todos whose title contains a text.")
@http(method: "GET", uri: "/todos/search", code: 200)
@timeout(milliseconds: 2000)
@readonly
operation SearchTodos {
```

`@timeout` reaches the handler as a `[Timeout]`. The served document repeats it as
`x-hardened-timeout`. [Request timeouts](/guide/request-timeouts) covers budgets. `@timeout` with
`milliseconds` of zero or less gives warning `HSMT006`. The operation then has no budget.

`@validation("stop-on-first-error")` answers a request that fails validation with its first failure
only. `@validation("collect-all")`, and an operation without the trait, answer with every failure.
Here the template's `src/Todos/contracts/todos.smithy` sets it on `CreateTodo`:

```smithy
use hardened.api#validation

@documentation("Creates a todo.")
@http(method: "POST", uri: "/todos", code: 201)
@validation("stop-on-first-error")
operation CreateTodo {
```

`@validation` reaches the handler as a `[ValidationMode]`, and the served document repeats it as
`x-hardened-validation`. [Validation](/guide/validation) covers the mode. The trait beats a
`[ValidationMode]` on the module class. [Generating from OpenAPI](/guide/openapi) covers how it meets
a `[ValidationMode]` on the implementation.

The CLI refuses any other value, and the build stops with `HSMT012`:

```text
TraitValue on com.example.todos#CreateTodo: Error validating trait `hardened.api#validation`: String value provided for `hardened.api#validation` must be one of the following values: `collect-all`, `stop-on-first-error`
```

A committed AST is not checked by the CLI. Another value there gives warning `HSMT006`, and the
operation answers with every failure.

## Constraints

The constraint traits map to these attributes:

| Trait | Attribute |
|---|---|
| `@required` | `[Required]`, except on a value type such as `Integer` |
| `@length` on a string | `[StringLength(Min = , Max = )]` |
| `@length` on a list or a map | `[ItemCount(Min = , Max = )]` |
| `@range` | `[Range(Min = , Max = )]` |
| `@pattern` | `[Pattern(typeof(<Model>Patterns), nameof(...))]` |

A constraint trait can sit on the member or on the shape it targets. The member's trait wins where
both declare one.

A `@pattern` member has a match timeout of 2,000 milliseconds, as an OpenAPI `pattern` does.

`@pattern` on an `@httpLabel` member is checked like any other constraint. A label value that does
not match answers 400 naming the member, as it does on a body member. The pattern is part of the
route only where two operations under one method have URIs that are the same apart from the names
of their labels. [Generating from OpenAPI](/guide/openapi#constraints) covers that case.

The checks run before the handler, as for an OpenAPI document.
[Generating from OpenAPI](/guide/openapi) covers the parameter interfaces and the 422.
[Validation](/guide/validation) covers the 400 body.

## Paging

`@paginated` names the members that carry a page: the input's token, the output's token, the
input's page size and the output's items. They generate as ordinary members. Here the interface
method takes the token and the page size as query parameters, and the output carries the next
token:

```smithy
@http(method: "GET", uri: "/todos/paged", code: 200)
@readonly
@paginated(inputToken: "pageToken", outputToken: "nextPageToken", pageSize: "pageSize", items: "todos")
operation ListTodosPaged {
    input := {
        @httpQuery("pageToken")
        pageToken: String

        @httpQuery("pageSize")
        @range(min: 1, max: 100)
        pageSize: Integer
    }
    output := {
        @required
        todos: TodoList

        nextPageToken: String
    }
}
```

```csharp
Task<ListTodosPagedOutput> ListTodosPaged(string? pageToken, int? pageSize);
```

The handler writes the paging. It takes `IPageTokens` through its constructor and builds the page
with `Page.From`, as [Paging](/guide/paging) shows for a code-first handler. A token that does not
decode answers 400 naming the parameter, here `pageToken`.

A `@paginated` on the service supplies each setting that an operation's own trait leaves out. An
operation with no `@paginated` of its own is not paged.

The build checks the members that the trait names:

| Setting | Names | Which targets |
|---|---|---|
| `inputToken` | A member of the input that is not `@required` | A `string` or a `map` |
| `outputToken` | A member of the output, or a path through nested structures such as `page.next` | A `string` or a `map` |
| `pageSize` | A member of the input | A `byte`, `short`, `integer` or `long` |
| `items` | A member of the output, or a path as for `outputToken` | A `list` or a `map` |

`inputToken` and `outputToken` are required, on the operation or on the service. These are the
rules that the Smithy CLI reports as errors. The CLI refuses a model that breaks one with `HSMT012`,
so a model built from `.smithy` files never reaches the build's own check. A committed AST does,
and each broken rule is error `HSMT035`:

```text
src/Todos/contracts/todos.json : error HSMT035: Operation 'ListTodosPaged' is @paginated, and its inputToken 'cursor' names no member of its input. The members generate as ordinary members, so the operation would build and not page the way the model says.
```

`IPageTokens` writes a string. A `map` token builds, and the handler fills it.

## Traits the build does not model

| Traits | Result |
|---|---|
| `@httpResponseCode`, `@httpPrefixHeaders` and `@httpQueryParams` | Warning `HSMT006`: "has no equivalent in the generated code". The member they mark is an ordinary member |
| A trait from the `smithy.api` prelude that the build neither reads nor ignores | Warning `HSMT006`: "which this generator does not model; it was ignored" |
| A trait that changes nothing a server does: documentation and metadata such as `@examples` and `@since`, client concerns such as `@idempotencyToken` and `@endpoint`, and `@cors`, `@sensitive` and the `xml` traits | Ignored without a warning |

## Serving the OpenAPI document

`PublishUrl`, `UiUrl` and `UiEnvironments` on the `HardenedSmithyModel` item serve the OpenAPI
document that the build generates from the model, and a reference page. `SourceUrl` serves the AST
file. It works on a `HardenedSmithyAst` item only. [The OpenAPI document](/guide/openapi-document)
covers all four.

The served document takes its title from the service's `@title` and its version from the service's
`version`.

## Build properties

| Property | Default | Effect |
|---|---|---|
| `HardenedSmithyCliVersion` | `1.73.0` | The CLI version the build is pinned to |
| `HardenedSmithyPinCliVersion` | `ContinuousIntegrationBuild`, else `false` | `true` makes a version mismatch an error |
| `HardenedSmithyCliPath` | The CLI on `PATH` | The CLI to run |
| `HardenedSmithyModelName` | The project's name | The name of the AST, the generated file and the helper types |
| `HardenedSmithyNamespace` | `RootNamespace` | The root of `.Models`, `.Services`, `.Validation` and `.Generated` |
| `HardenedSmithyServiceShapeId` | Every service | An absolute shape id, such as `com.example.lab#Widgets`. Only that service is generated |
| `HardenedSmithyEmbedDocument` | `true` | Whether the AST is embedded, for `SourceUrl` |
| `HardenedSmithyEmitUnreferencedSchemas` | `false` | `true` generates shapes no operation reaches |
| `HardenedResponseModel` | `Throws` | `Throws`, `Response` or `Union`. [Generating from OpenAPI](/guide/openapi) covers it |
| `HardenedBindCancellationToken` | `false` | `true` adds a `CancellationToken` to every method. [Request timeouts](/guide/request-timeouts) covers it |
| `ExcludeGeneratedCodeFromCoverage` | `true` | `[ExcludeFromCodeCoverage]` on the generated types |

A `HardenedSmithyServiceShapeId` that the model does not declare stops the build with `HSMT002`.
`HardenedSmithyNamespace=Acme.Widgets` puts the generated types in `Acme.Widgets.Models` and
`Acme.Widgets.Services`.

## Diagnostics

| Code | Severity | Reported when |
|---|---|---|
| `HSMT002` | Error | The model cannot be read, declares no service or no operation, uses a refused protocol, or lacks the service `HardenedSmithyServiceShapeId` names |
| `HSMT003` | Error | A `.smithy` file is named by `HardenedSmithyAst` |
| `HSMT004` | Error | A generated model or source file is missing |
| `HSMT006` | Warning | A member narrows to `decimal` or `long`, a trait is not modeled, an operation is skipped, or a committed AST gives `@validation` a value other than `stop-on-first-error` or `collect-all` |
| `HSMT010` | Error | The Smithy CLI is not found |
| `HSMT011` | Warning, or error when pinned | The CLI's version is not `HardenedSmithyCliVersion` |
| `HSMT012` | Error | The CLI refuses the model |
| `HSMT013` | Warning | The CLI warns about the model |
| `HSMT014` | Error | The CLI succeeds and writes no AST |
| `HSMT015` | Error | `HardenedSmithyModel` items disagree on `PublishUrl` or `UiUrl` |
| `HSMT031` | Warning | The served document puts two operations at one path and method |
| `HSMT035` | Error | A committed AST's `@paginated` names a member the operation does not have, or one of the wrong type. See [Paging](#paging) |

The build task's codes that Smithy and OpenAPI projects share, from 020 up, take the `HSMT` prefix
in a Smithy project, where an OpenAPI project reports `HOAT`. [Generating from OpenAPI](/guide/openapi)
lists them. The generator's codes keep the `HOAG` prefix in both. `HOAG030`, when no `[Handler]`
class implements the service, is one. [Diagnostics](/reference/diagnostics) lists every code.

## Limits

::: v-pre

- A `union` is read and written as the member's value alone. The object that Smithy's JSON
  protocols send, `{"circle": {...}}`, answers 400.
- `@timestampFormat` is not applied. A `Timestamp` is an RFC 3339 string whatever the trait says.
- `@default`'s value is not applied to a structure member. A request that leaves the member out
  reads null.
- A constraint trait other than `@length`, `@range` and `@pattern` on a named `string` or `list`
  shape is neither enforced nor reported. These traits are `@enum`, `@uniqueItems` and `@sparse`.
- Under an awsJson protocol, the response is sent as `Content-Type: application/json`. The served
  document keeps only one of the operations at `POST /`.
- A route attribute in the project is not served, as for an OpenAPI document.
- `smithy.framework#ValidationException` is not mapped. The build runs `smithy ast` over the
  project's own `.smithy` files, so the shape has to be declared in one of them. A validation failure
  on an operation that declares it at 400 answers the body the build fills for that shape, which
  carries no field errors. They are in the request log. An operation that declares no 400 answers
  the framework's `RequestValidationError`, which lists them.

:::

## Next

- [Generating from OpenAPI](/guide/openapi): the generated interface, the response models and the
  implementation's attributes
- [The OpenAPI document](/guide/openapi-document): the served document and the reference page
- [Request timeouts](/guide/request-timeouts): `@timeout` and budgets
- [Authorization](/guide/authorization): grants a Smithy model cannot state
- [Project templates](/guide/project-templates): `--contract smithy`

# Generating from OpenAPI

A `HardenedOpenApiSpec` item in the project file names an OpenAPI document. The build reads the
document and generates the models, a service interface for each tag, a handler for each operation,
the routing table and the request validation.

`src/Todos/Todos.csproj` declares the document:

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
    <PackageReference Include="Hardened.OpenApi.SourceGenerator" PrivateAssets="all" />
  </ItemGroup>

  <ItemGroup>
    <HardenedOpenApiSpec Include="contracts\todos.yaml" />
  </ItemGroup>
</Project>
```

`HardenedResponseModel` chooses the shape of each method's return type. The example uses
`Response`, which is what `dotnet new hardened-web --contract openapi` writes.
[Response models](#response-models) covers the three models.

The document is `src/Todos/contracts/todos.yaml`:

```yaml
openapi: "3.0.0"
info:
  title: Todos API
  version: "1.0.0"
paths:
  /todos/{id}:
    get:
      tags:
        - Todos
      operationId: getTodo
      summary: One todo by id.
      parameters:
        - name: id
          in: path
          required: true
          schema:
            type: integer
            format: int32
            minimum: 1
      responses:
        '200':
          description: The todo.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Todo'
        '404':
          description: No todo has that id.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Problem'
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
        title:
          type: string
        done:
          type: boolean
    Problem:
      type: object
      properties:
        type:
          type: string
        title:
          type: string
        status:
          type: integer
          format: int32
        detail:
          type: string
```

The build generates `ITodosService` for the `Todos` tag. This excerpt of
`obj/Debug/net8.0/openapi/generated/todos.g.cs` leaves out the `global::` qualifiers:

```csharp
namespace Todos.Services
{
    public partial interface ITodosService
    {
        /// <summary>
        /// GET /todos/{id} → 200
        ///
        /// One todo by id.
        /// </summary>
        Task<GetTodoResponse> GetTodo(int id);
    }
}
```

A class that carries `[Handler]` and implements the interface answers the document's operations.
The class declares no route attributes. The application does not register it. `[Handler]` is in
`Hardened.Requests.Abstract.Attributes`. `ITodoStore` is the template's store.

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

The class answers the two statuses that the document declares:

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

{"type":"urn:hardened:problem:not-found","title":"Not Found","status":404,"detail":"No todo has id 99."}
```

The 404 carries the document's `Problem` schema. The build converts the framework's `NotFound` into
it. [Response and Union](#response-and-union) gives the rule.

An operation that the document adds and the class does not implement fails the build with `CS0535`.

## Declaring the document

The project that holds the document references five packages and leaves out a sixth:

| Package | Referenced |
|---|---|
| `Hardened.Shared.Runtime` | Yes |
| `Hardened.Web.Runtime` | Yes |
| `Hardened.Library.SourceGenerator` | Yes |
| `ValidationModules.SourceGenerator` | Yes |
| `Hardened.OpenApi.SourceGenerator` | Yes, with `PrivateAssets="all"` |
| `Hardened.Web.SourceGenerator` | No |

The references carry no version. The template's `Directory.Packages.props` sets the versions.
[Project templates](/guide/project-templates) covers the file.

`Hardened.OpenApi.SourceGenerator` brings `Hardened.Idl.SourceGenerator`, which generates the
handlers and the routing table. With `Hardened.Web.SourceGenerator` in the same project, the build
fails with `HRDR008` and `CS0102` in the generated files. `PrivateAssets="all"` keeps the package's
generator out of the projects that reference this one. Without it, a host project that references
`Hardened.Web.SourceGenerator` fails with `HRDR008`.

Without `ValidationModules.SourceGenerator`, every request to an operation with a constraint
answers 500. The build reports nothing.

The module class carries `[HardenedModule]` and `[HardenedWebModule]`:

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Todos;

[HardenedModule]
[HardenedWebModule]
public partial class TodosLibrary;
```

`dotnet new hardened-web --contract openapi` writes this project.
[Project templates](/guide/project-templates) covers the option.

A project can declare several documents, each in its own item. The file name of each document, in
Pascal case, prefixes the helper types that the build writes for it. `todos.yaml` gives
`TodosProblems` and `TodosErrorBodies`.

The document can be YAML or JSON. The build decides by the first character that is not white space.
A `{` means JSON.

An item that names a file that does not exist stops the build with `HOAT001`. A `.yaml` file
declared as `AdditionalFiles`, and not as a `HardenedOpenApiSpec` item, stops the build with
`HOAT003`.

## What the build generates

The build writes the C# for each document into one file,
`obj/<configuration>/<tfm>/openapi/generated/<file>.g.cs`. It writes the handlers, the routing table
and the links under `obj/<configuration>/<tfm>/generated/`. `Hardened.OpenApi.SourceGenerator` sets
`EmitCompilerGeneratedFiles` to `true` unless the project sets it.

The generated types go under the project's root namespace, or under `HardenedOpenApiNamespace` when
the project sets it. The table lists the generated names that an implementation meets. It writes
that namespace as `<Root>`, and the document's file name in Pascal case as `<File>`.

| Name | For | Namespace |
|---|---|---|
| `I<Tag>Service` | Each tag | `<Root>.Services` |
| `<Schema>` | Each schema an operation reaches | `<Root>.Models` |
| `<Method>Response` | An operation that answers with a response set. See [Response models](#response-models) | `<Root>.Models` |
| `<Method><Status>` | A success case that needs a type of its own: `CreateTodoCreated`, `RemoveTodoNoContent`, `GetJobAccepted` | `<Root>.Models` |
| `I<Method>Parameters` | An operation with a constraint. See [Constraints](#constraints) | `<Root>.Validation` |
| `<File>Patterns` | The `pattern`s the document declares | `<Root>.Validation` |
| `<File>Errors` | `AsException()` for an error the build generates a type for. See [Throws](#throws) | `<Root>.Models` |

An operation goes to the interface of its first tag. Operations with no tag go to one interface,
`IDefaultService`. Each interface is `partial`.

The method is named for the `operationId` in Pascal case. An operation with no `operationId` is
named for its verb and path, with each path parameter as `By<Name>`. `GET /orders/{id}` gives
`GetOrdersById`. The method's doc comment gives the verb, the path and the success status. It then
gives the operation's `summary`, or its `description` when there is no summary.

The parameters come in the order that the document declares them. Parameters declared on the path
item come first. The request body comes last, as `body`. A header parameter is named from the
header: `X-Region` gives `xRegion`. A parameter that is not `required` is nullable.

The models are `partial` records, one for each schema that an operation reaches. A record is
`sealed` unless another schema derives from it. Each property is a positional parameter that carries
`[JsonPropertyName]` with the document's name. The build does not generate a schema that no
operation reaches. `HardenedOpenApiEmitUnreferencedSchemas` set to `true` generates it.

The build writes a handler class for each operation in `<Root>.Generated`, named
`<Tag>Controller_<Method>`. It also generates `Routes` and `Links` for the module.
[Route links](/guide/route-links) covers them.

## Types

Each schema becomes a C# type:

| Schema | C# type |
|---|---|
| `type: string` | `string` |
| `type: string`, `format: date-time` | `DateTimeOffset` |
| `type: string`, `format: date` | `DateOnly` |
| `type: string`, `format: byte` or `binary` | `byte[]` |
| `type: string`, `format: uuid` | `string` |
| `type: string`, `format: number` | `decimal` |
| `type: integer` | `int`, or `long` when a declared bound does not fit an `int` |
| `type: integer`, `format: int64` | `long` |
| `type: integer`, `format: uint32` | `uint` |
| `type: number` | `double` |
| `type: number`, `format: float` | `float` |
| `type: number`, `format: decimal` | `decimal` |
| `type: boolean` | `bool` |
| `type: array` | `List<T>` |
| `type: object` with `additionalProperties` and no `properties`, inline or through a `$ref` | `Dictionary<string, T>` |
| `type: object` with `properties` | A record. An object written inline is named for its parent and the property: `TypeZooNested` |
| A schema with `enum`, under `components/schemas` | A C# `enum` |
| An `enum` written inline on a property | The member's type, with `[AllowedValues]` |
| `oneOf` that a `discriminator` or the branches' shapes can decide | A struct holding one branch |
| `allOf` | One record with the properties of every branch |
| A schema with no `type` | `JsonElement` |

A property that is not in `required` becomes a nullable member whose parameter defaults to
`default`. A property that is `required` and nullable becomes a nullable member with no default. The
record's parameters without a default come first. A required member carries `[JsonRequired]`. A
request that leaves it out answers 400 with the code `required`.

An optional member that the schema does not declare nullable is left out of a response when it is
null. A member that the schema declares nullable is written as `null`. A 3.0 document declares
nullable with `nullable: true`. A 3.1 or later document uses `type: [string, "null"]`. In a 3.1 or
later document, the build ignores `nullable` and reports warning `HOAT032`.

A generated `enum` has a member for each value. The member names are the values in Pascal case:
`dark-blue` is `DarkBlue`. The JSON reads and writes the values. An integer `enum` takes its member
names from `x-enum-varnames` or `x-enumNames`. Without them the members are `Value1`, `Value2` and so
on. An `enum` that mixes strings and numbers stops the build with `HOAT023`.

The struct for a `oneOf` has a constructor and an implicit conversion for each branch, and
`object? Value`. A `oneOf` with a `discriminator` reads the payload by the discriminator's value. A
value that no mapping names answers 400. The branches' shapes decide a `oneOf` without a
discriminator. Where the shapes overlap, the build reports warning `HOAT022`. A payload that fits
more than one branch is refused. A `oneOf` whose branches nothing separates is `JsonElement`.

`readOnly` puts `[ResponseOnly]` on the member. `writeOnly` puts `[RequestOnly]` on it. Neither
attribute is enforced. A request can set a `readOnly` member. A response writes a `writeOnly`
member.

A `deprecated` schema or operation gets `[Obsolete]`.

A success response whose schema is a map, inline or through a `$ref`, is `JsonElement`. The method
returns `Task<JsonElement>`. The map row in the table applies to properties.

A property whose name would equal its record's name is renamed: `thing` on `Thing` is `ThingThing`.
The JSON name does not change. The build reports warning `HOAT020`.

A `$ref` to a schema that the document does not declare stops the build with `HOAT027`.

## Response models

`HardenedResponseModel` in the project file chooses how each operation's statuses reach its
signature. Its values are `Throws`, `Response` and `Union`. Without the property, the model is
`Throws`. An unrecognised value is also `Throws`. Changing the property rewrites every generated
signature.

| Model | Return type | A declared error | Needs |
|---|---|---|---|
| `Throws` | The success payload: `Task<Todo>` | Thrown | `net8.0` or later |
| `Response` | `Task<GetTodoResponse>`, a generated struct | Returned, as a case | `net8.0` or later |
| `Union` | `Task<GetTodoResponse>`, a generated C# `union` | Returned, as a case | `net11.0` and `<LangVersion>preview</LangVersion>` |

In every model, the operation answers the success status that the document declares. An operation
that declares `201` answers 201.

These return types are the same in every model:

| The operation declares | Return type |
|---|---|
| One success, and no error or response header | The payload: `Task<List<Todo>> ListTodos()` |
| Two 2xx statuses | A response set: `GetJobResponse`, with `Job` for the 200 and `GetJobAccepted(JobProgress Body)` for the 202 |
| A success with a response header | A response set. The success case takes the header as a `string` after the body: `CreateTodoCreated(Todo Body, string Location)` |

An operation that declares no success body, and answers with no response set, returns `Task`, such
as `Task RemoveTodo(int id)` under `Throws`. The build ignores a `2XX` or `4XX` range and a
`default` response.

A declared error becomes a case:

| The document declares | The case |
|---|---|
| A status with a body, such as 404 with `Problem` | The shipped generic record: `NotFound<Problem>` |
| A status with no body, such as 410 | The shipped record: `Gone` |
| A registered status with no shipped record, such as 423 | `Status<Http.Locked, Problem>` |
| A response under `components/responses`, such as `PetMissing` | A generated type named for the key: `PetMissing`, or `PetMissingException` under `Throws` |
| An error that declares a header, such as 429 with `Retry-After` | A generated type: `RateLimitedLabProblem(LabProblem Body, string RetryAfter)`, or `RateLimitedLabProblemException` under `Throws` |
| A status nothing registers, such as 529 | A generated type: `Status529LabProblem`, or `Status529LabProblemException` under `Throws` |

The shipped records and `Http` are in `Hardened.Web.Runtime.Responses`.
[Declared responses](/guide/responses) lists them. Two operations that declare the same error over
the same schema share one type.

### Throws

A `GET` or `PUT` operation that declares a 404, and whose success is a `$ref`, returns a nullable
payload such as `Task<Todo?>`. Returning null answers 404. The 404 body is the declared schema, with
`type`, `title` and `status` filled when the schema has them. When the schema has another required
member with no `default`, the 404 for a null return has no body. The payload is never nullable for a
`POST`, `DELETE` or `PATCH` operation, or for an array success.

The handler answers a declared error by throwing it. `AsException()` turns a shipped record into an
exception. The handler throws a generated exception type with `new`. The method's doc comment lists
what it may throw, such as `Throws NotFound<Problem>.` Where one schema is the body of only one
generated error, the build also generates `AsException()` on that schema, in `<File>Errors`.

Without `HardenedResponseModel` in its project file, the example's `GetTodo` returns `Task<Todo?>`:

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Web.Runtime.Responses;
using Todos.Models;
using Todos.Services;

namespace Todos;

[Handler]
public class TodoService(ITodoStore store) : ITodosService
{
    public async Task<Todo?> GetTodo(int id)
    {
        if (id > 1000)
        {
            var problem = new Problem(Title: "Archived", Status: 404, Detail: $"Todo {id} was archived.");

            throw new NotFound<Problem>(problem).AsException();
        }

        return await store.Find(id);
    }
}
```

For `GET /todos/99` the method returns null. For `GET /todos/1001` it throws:

```http
GET /todos/99

HTTP/1.1 404 Not Found
Content-Type: application/json

{"type":"urn:hardened:problem:not-found","title":"Not Found","status":404}
```

```http
GET /todos/1001

HTTP/1.1 404 Not Found
Content-Type: application/json

{"title":"Archived","status":404,"detail":"Todo 1001 was archived."}
```

### Response and Union

An operation that declares an error returns `<Method>Response`. It holds one case for each declared
status: the success payload, then each error's case. Under `Response` it is a struct with a
constructor and an implicit conversion for each case, and `object? Value`. Under `Union` it is a C#
`union` declaration with the same cases. The handler's code is the same in both models.

A case that the operation does not declare does not convert. Returning it is a compile error. A
success with no body is a case type of its own, such as `<Method>NoContent` for a 204. The handler
returns it with `return new RemoveTodoNoContent();`.

A shipped record without a type argument, such as `NotFound`, converts into the declared case when
the declared body has a string `title` and an integer `status`, and every other required member can
be filled. The build fills `type`, `title` and `status` from the record, and `detail` from its
`Detail`. A record's static `Default`, such as `NotFound.Default`, converts too. It sends the
record's general detail. The conversion methods are in `<File>Problems`, such as
`TodosProblems.NotFoundProblem`.

A declared body of any other shape has no conversion. The handler returns the case itself, such as
`new Conflict<ApiError>(...)`. A generated case, for a `components/responses` key, a header or an
unregistered status, has no conversion either.

The template's `TodoService` also has `CreateTodo`, which needs the same `using` lines as the first
example. `CreateTodo` returns a `Conflict`, which converts to the declared 409. Its success case,
`CreateTodoCreated`, carries the `Location` header:

```csharp
public async Task<CreateTodoResponse> CreateTodo(NewTodo body)
{
    if (await store.TitleExists(body.Title))
    {
        return new Conflict($"A todo titled '{body.Title}' already exists.");
    }

    var created = await store.Add(body.Title);

    return new CreateTodoCreated(
        created,
        TodosLibrary.Routes.Todos.GetTodo(created.Id)
    );
}
```

The same request answers 201, and then 409:

```http
POST /todos
Content-Type: application/json

{"title":"Buy milk"}

HTTP/1.1 201 Created
Content-Type: application/json
Location: /todos/3

{"id":3,"title":"Buy milk","done":false}
```

```http
POST /todos
Content-Type: application/json

{"title":"Buy milk"}

HTTP/1.1 409 Conflict
Content-Type: application/json

{"type":"urn:hardened:problem:conflict","title":"Conflict","status":409,"detail":"A todo titled \u0027Buy milk\u0027 already exists."}
```

## Constraints

The build writes the document's constraints onto the generated members as attributes from
`ValidationModules.Constraints`. The checks run before the handler. [Validation](/guide/validation)
covers the attributes and the 400.

| Keyword | Attribute |
|---|---|
| `required` | `[Required]` |
| `minLength`, `maxLength` on a string | `[StringLength(Min = , Max = )]` |
| `minimum`, `maximum` on a number | `[Range(Min = , Max = )]` |
| `exclusiveMinimum`, `exclusiveMaximum` | `[Range]` with `ExclusiveMin = true` or `ExclusiveMax = true` |
| `pattern` on a string | `[Pattern(typeof(<File>Patterns), nameof(...))]` |
| `minItems`, `maxItems` on an array, `minProperties`, `maxProperties` on a map | `[ItemCount(Min = , Max = )]` |
| `enum` on an inline string | `[AllowedValues(...)]` |
| A property whose schema has constraints of its own | `[ValidateNested]` |

`required` adds no `[Required]` to a value type such as `int`, or to a path parameter. The build
drops a keyword that the member's type cannot carry, such as a `minimum` on a string.

A `pattern` becomes a `[GeneratedRegex]` member of `<File>Patterns`. A `pattern` on a path parameter
is part of the route. A path value that does not match answers 404, not 400. Each member has a
match timeout of 2,000 milliseconds, in the route and in validation.
[Validation](/guide/validation#a-timeout-on-a-pattern) covers the timeout.

An operation with a constraint gets `I<Method>Parameters`, with a property for each parameter and
`body` for the request body. The generated handler checks it.

A failure inside the body is named from `body`, such as `body.title` or `body.lines2[0].qty`. A
parameter's failure is named as the request names it, such as `q` or `X-Region`. The template's
`POST /todos` answers an empty title with 400:

```http
POST /todos
Content-Type: application/json

{"title":""}

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"body.title","code":"required","message":"title is required."}]}
```

An operation that declares a 422 answers every validation failure with 422, a malformed body
included.

The generated validation ignores `multipleOf`, `uniqueItems` and `not`. The build reports warning
`HOAT024`, which names the keyword. The same applies to constraints on an array's inline items, such
as `items: {minLength: 2}`.

## Filters from the document

`x-filters` on an operation puts filter attributes on its handler. Each key names a filter type. The
key's object gives the property values. `x-filter-types` at the root declares each filter type: its
`namespace`, and its `properties` with a `type`, a `default` or an `enum`.

The build generates each declared type as a `partial` attribute class named `<Name>Attribute`, with
a property for each declared property. The generated class does nothing on its own. A `partial`
declaration of the same class that implements `IRequestFilterProvider` gives it a filter.
[The execution pipeline](/guide/execution-pipeline) covers filters.

`generate: false` on a type declares a class that exists already, in a referenced library. The build
generates nothing for it.

This excerpt of `src/Todos/contracts/todos.yaml` declares an `Audit` type and applies it to
`getTodo`:

```yaml
x-filter-types:
  Audit:
    namespace: Todos.Filters
    properties:
      Category:
        type: string
        default: general
paths:
  /todos/{id}:
    get:
      tags:
        - Todos
      operationId: getTodo
      summary: One todo by id.
      x-filters:
        Audit:
          Category: catalog
```

`src/Todos/Filters/AuditAttribute.cs` gives the generated attribute its filter:

```csharp
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Todos.Filters;

public partial class AuditAttribute : IRequestFilterProvider
{
    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo)
    {
        var category = Category;

        yield return new RequestFilterInfo(
            _ => new AuditFilter(category),
            FilterOrder.DefaultValue,
            nameof(AuditFilter)
        );
    }
}

public class AuditFilter(string category) : IExecutionFilter
{
    public async Task Execute(IExecutionChain chain)
    {
        await chain.Next();

        chain.Context.Response.Headers["X-Audit"] = category;
    }
}
```

The operation's response carries the filter's header:

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json
X-Audit: catalog

{"id":1,"title":"Read the generated code","done":true}
```

## Attributes on the implementation

An attribute that implements `IRequestFilterProvider` applies to the operation when it is on the
implementation's method or class. `[AuthorizeGrants]` on the implementation's method adds its grants
to what the document requires.

::: warning
`[AllowAnonymous]` on the implementation's method makes the operation public, whatever the
document's `security` says. The build reports nothing. The served document still declares the
operation's `security` and its 401.
:::

`[Throws<T>]` and `[RawResponse]` on a method, and `[Tag]` and `[Server]` on the class, change
nothing. The build reports warning `HOAG032` for each. The document states each of them instead:

| Attribute | Written in the document as |
|---|---|
| `[Throws<T>]` | The operation's `responses` |
| `[RawResponse]` | The response's media type |
| `[Tag]` | The operation's `tags` |
| `[Server]` | A `servers` block |

A `[Handler]` class whose base list names no interface that the document declares gets warning
`HOAG031`. A declared interface that no `[Handler]` class implements gets warning `HOAG030`. Its
routes fail at request time.

## Security

`security`, on an operation or at the root, becomes the requirement that the operation checks before
the handler runs. [Authorization](/guide/authorization) covers requirements. The build reads the
requirement from the scopes. The scheme decides only whether an entry carries scopes.

The operation does not check which scheme established the caller. The application's authentication
supplies the caller, as [Authentication](/guide/authentication) describes.

| Declaration | Requirement |
|---|---|
| `oauth2: ['pets:read']` | The grant `pets:read` |
| `oauth2: ['pets:read', 'pets:write']` | Both grants |
| `apiKey: []`, or any scheme with no scopes | An authenticated caller |
| `[{oauth2: ['pets:admin']}, {apiKey: []}]` | Either entry |
| `{oauth2: ['pets:admin'], apiKey: []}`, one entry | Every scheme in it |
| `security: []` on an operation | Nothing from the document, whatever the root says |
| No `security` on the operation | The root's `security`, or nothing |

The operation answers 401 to a caller with no credentials. It answers 403 to a caller without the
grant.

Only `oauth2` and `openIdConnect` schemes carry scopes. The build does not read scopes on another
scheme. The operation then requires an authenticated caller. The build reports warning `HOAT006`.
An entry that names a scheme missing from `components.securitySchemes` also requires an
authenticated caller, with warning `HOAT006`.

`security: []` does not make an operation public under `[RequireAuthorization]`.
[Authorization](/guide/authorization) covers the attribute.

The served document repeats each operation's `security` and the declared schemes. It adds the 401
and 403 that the requirement answers.

## Extension keys

The build reads these `x-` keys:

| Key | Where | Effect | Covered by |
|---|---|---|---|
| `x-filters` | An operation | Filter attributes for the operation's handler | [Filters from the document](#filters-from-the-document) |
| `x-filter-types` | The root | The filter attribute types that `x-filters` names | [Filters from the document](#filters-from-the-document) |
| `x-hardened-timeout` | An operation | The operation's time budget, in milliseconds, or an object with `milliseconds`, `status` and `retryAfterSeconds` | [Request timeouts](/guide/request-timeouts) |
| `x-hardened-validation` | An operation | `stop-on-first-error` answers a request that fails validation with its first failure only. `collect-all` answers with every failure | [Validation](/guide/validation) |
| `x-hardened-raw-bytes` | An operation | `true` makes the method return `byte[]` for a response the document types as a string | This page |
| `x-codegen-exclude` | A parameter | `true` leaves the parameter out of the signature, the binding and the served document | This page |
| `x-enum-varnames`, `x-enumNames` | An `enum` schema | The C# member names, in the order of the values | [Types](#types) |
| `x-hardened-content-negotiation` | The root | `strict` or `lenient`, the service's negotiation policy | [Content negotiation](/guide/content-negotiation) |
| `x-hardened-error-bodies` | The root | `json` answers every failure as JSON, or `negotiated` | [Content negotiation](/guide/content-negotiation) |
| `x-message-pack-index` | A property | The member's key under `HardenedSerializer` `MessagePackKeyed` | [MessagePack](/guide/message-pack) |

With `x-hardened-raw-bytes: true` on its operation, `GetRaw()` returns `Task<byte[]>`.

An `x-hardened-timeout` of zero or less stops the build with `HOAT002`. The key reaches the handler
as a `[Timeout]`. The served document repeats the key, with a 504 response.
[Request timeouts](/guide/request-timeouts) covers budgets, and how the implementation reaches its
`CancellationToken`.

`x-hardened-validation: stop-on-first-error` answers a request that fails validation with its first
failure only. `collect-all`, and an operation without the key, answer with every failure. Here the
template's `src/Todos/contracts/todos.yaml` sets it on `createTodo`:

```yaml
paths:
  /todos:
    post:
      tags:
        - Todos
      operationId: createTodo
      summary: Creates a todo.
      x-hardened-validation: stop-on-first-error
```

The key reaches the handler as a `[ValidationMode]`, and [Validation](/guide/validation) covers the
mode. The key beats a `[ValidationMode]` on the module class and one on the implementation's method.
On an operation whose document declares no mode, a `[ValidationMode]` on the implementation sets it.
The served document repeats the key. Any other value stops the build with `HOAT002`, which names the
operation and the value:

```text
'createTodo' declares an x-hardened-validation of "first-error". It has to be stop-on-first-error or collect-all.
```

`x-hardened-content-negotiation: lenient` and `x-hardened-error-bodies: json` register the same
policies as the `[ContentNegotiation]` and `[JsonErrorBodies]` attributes.

The build does not apply an `x-filters` key that no `x-filter-types` entry declares. The build
reports nothing.

## Serving the document

`PublishUrl`, `SourceUrl` and `UiUrl` on the item serve the document that the build generated, the
document file as written, and a reference page. `UiEnvironments` limits the reference page to the
environments it names. [The OpenAPI document](/guide/openapi-document) covers all four.

## Slicing a document

`Tags`, `IncludePaths` and `ExcludePaths` on the item keep only the operations that they select.
Each takes a list separated by semicolons.

| Metadata | Effect |
|---|---|
| `Tags` | Keeps an operation whose first tag is in the list |
| `IncludePaths` | Keeps an operation whose path matches a pattern |
| `ExcludePaths` | Removes an operation whose path matches a pattern |
| `Slice` | Names the slice. The name is added to the generated file's name and to the helper types' prefix |

`*` matches within one segment of a path pattern. `**` matches any number of segments. The build does
not generate a schema that the kept operations do not reach. A slice that keeps no operation stops
the build with `HOAT007`.

The `Slice` name lets two slices of one document sit in one project. This item keeps the operations
whose first tag is `Secure`:

```xml
<ItemGroup>
  <HardenedOpenApiSpec Include="contracts\lab.yaml" Tags="Secure" Slice="secure" />
</ItemGroup>
```

For this item the build writes `lab.secure.g.cs` and `LabSecureSpecification`.

A sliced document that is embedded gets warning `HOAT009`. `SourceUrl` would serve operations that
the application does not implement. `EmbedDocument="false"` on the item removes the warning.

## Build properties

The build reads these properties from the project file:

| Property | Default | Effect |
|---|---|---|
| `HardenedResponseModel` | `Throws` | `Throws`, `Response` or `Union`. See [Response models](#response-models) |
| `HardenedOpenApiNamespace` | `RootNamespace` | The root of `.Models`, `.Services`, `.Validation` and `.Generated` |
| `HardenedOpenApiApplyServerBasePath` | `false` | `true` puts the path of the first `servers` URL in front of every route |
| `HardenedOpenApiGroupUntaggedByPath` | `false` | `true` puts operations with no tag into an interface for each first path segment |
| `HardenedOpenApiEmitUnreferencedSchemas` | `false` | `true` generates schemas no operation reaches |
| `HardenedOpenApiEmbedDocument` | `true` | Whether the document file is embedded, for `SourceUrl`. [The OpenAPI document](/guide/openapi-document) covers it |
| `HardenedBindCancellationToken` | `false` | `true` adds a `CancellationToken` to every method. [Request timeouts](/guide/request-timeouts) covers it |
| `ExcludeGeneratedCodeFromCoverage` | `true` | `[ExcludeFromCodeCoverage]` on the generated types and handlers |

`HardenedResponseModel` and `HardenedBindCancellationToken` take effect on the next build. The other
properties take effect after the document file changes or `obj` is deleted.

`HardenedOpenApiNamespace` is a plain property, set in a `PropertyGroup`:

```xml
<PropertyGroup>
  <HardenedOpenApiNamespace>Contoso.Petstore.Api</HardenedOpenApiNamespace>
</PropertyGroup>
```

With `servers: [{url: https://api.example.com/v1}]`, `HardenedOpenApiApplyServerBasePath` makes the
routes answer under `/v1`. The served document then lists `https://api.example.com` as its server.

With `HardenedOpenApiGroupUntaggedByPath`, `GET /orders/{id}` and `GET /customers` go to
`IOrdersService` and `ICustomersService`.

## When the build generates nothing

Every build writes `_SpecModelDiagnostic.g.cs` among the generated files. The file counts the
documents that the compiler received and lists their model files. `Total AdditionalTexts: 0` means
that no document reached the compiler. A project with the package and no `HardenedOpenApiSpec` item
shows it:

```csharp
// OpenAPI Generator Diagnostic
// Total AdditionalTexts: 0
// OpenAPI files parsed: 0
// AdditionalText paths:
//   (none)
```

In the example project, the file lists one model file. The path is shortened here:

```csharp
// OpenAPI Generator Diagnostic
// Total AdditionalTexts: 1
// OpenAPI files parsed: 1
// AdditionalText paths:
//   /src/Todos/obj/Debug/net8.0/openapi/todos.openapi-model.txt
```

A document that cannot be read stops the build with `HOAT002`. The message gives the reason. The
build reports a problem in a document that it could read as warning `HOAT006`. A model file that the
generator cannot read is warning `HOAG002`.

## Diagnostics

The build and the generator report these codes:

| Code | Severity | Reported when |
|---|---|---|
| `HOAT001` | Error | The item's file does not exist |
| `HOAT002` | Error | The document cannot be read, an `x-hardened-timeout` is zero or less, or an `x-hardened-validation` is not `stop-on-first-error` or `collect-all` |
| `HOAT003` | Error | A `.yaml` is declared as `AdditionalFiles` |
| `HOAT004` | Error | A generated model or source file is missing. Delete `obj/.../openapi/` and rebuild |
| `HOAT006` | Warning | The reader reports a problem in a document it could read, or `security` names scopes it cannot read |
| `HOAT007` | Error | A slice keeps no operation |
| `HOAT009` | Warning | A sliced document is embedded |
| `HOAT016` | Error | `UiUrl` without `PublishUrl` |
| `HOAT017` | Error | `SourceUrl` with `EmbedDocument` off |
| `HOAT020` | Warning | A property would share its record's name, and is renamed |
| `HOAT022` | Warning | A `oneOf` with no discriminator is decided by shape |
| `HOAT023` | Error | An `enum` mixes strings and numbers |
| `HOAT024` | Warning | A constraint keyword is not enforced |
| `HOAT026` | Warning | A path names a parameter the operation does not declare |
| `HOAT027` | Error | A `$ref` names a schema the document does not declare |
| `HOAT032` | Warning | `nullable` in a 3.1 or later document |
| `HOAG002` | Warning | A model file the generator cannot read |
| `HOAG030` | Warning | No `[Handler]` class implements a declared interface |
| `HOAG031` | Warning | A `[Handler]` class implements no declared interface |
| `HOAG032` | Warning | An attribute on the implementation that the document states instead |

[Diagnostics](/reference/diagnostics) lists every code. `HOAT033` and `HOAT034` concern MessagePack
keys. [MessagePack](/guide/message-pack) covers them.

## Limits

- A request body declared `application/x-www-form-urlencoded` or `multipart/form-data` is read as
  JSON. The served document still declares the form. See [Forms and files](/guide/forms).
- A binary request body is read as JSON. A body declared `application/octet-stream` with
  `format: binary` generates a `string` parameter. Raw bytes answer 400.
- A route attribute in a project that holds a document is not served. The build reports nothing.
  Code-first handlers go in another project.
- Two documents in one project that declare a schema of the same name fail the build with `CS0579`
  and `CS8863` in the generated files.
- A schema's `default` is the record parameter's default. When a request leaves the member out, the
  member is null, not the default.
- A `$ref` into another file stops the build with `HOAT027`, with or without
  `HardenedOpenApiLoadExternalRefs`.
- A `pattern` that .NET's regex engine cannot compile, such as `^[a-z\_]+$`, is not enforced. The
  build reports nothing.
- A `readOnly` or `writeOnly` direction is not enforced, as [Types](#types) describes.
- On the implementation of an operation whose document declares no validation mode, a
  `[ValidationMode]` on the class beats one on the method. A code-first handler takes the method's.

## Next

- [Generating from Smithy](/guide/smithy): the same generated output from a Smithy model
- [The OpenAPI document](/guide/openapi-document): the served document, the reference page and the
  export
- [Declared responses](/guide/responses): the shipped response records that a case is made from
- [Validation](/guide/validation): the constraint attributes and the 400 body
- [Request timeouts](/guide/request-timeouts): a budget declared in the document

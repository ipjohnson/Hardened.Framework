# Declared responses

A handler declares the statuses it answers in its return type. `Response<T1..Tn>` is the set, with
one case for each type argument.

This page covers code-first handlers. [Generating from OpenAPI](/guide/openapi) and
[Generating from Smithy](/guide/smithy) cover the interfaces a contract generates.

The `hardened-web` template writes this handler in `src/Todos/TodoController.cs`. The examples'
routes answer under `/todos`, the `[BasePath("/todos")]` on the template's library module.

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

public class TodoController
{
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

{"resource":"todo","detail":"No todo has id 99.","type":"urn:hardened:problem:not-found","title":"Not Found","status":404}
```

Each case's type carries its status. `NotFound` answers 404. The handler returns the value of one
case. An implicit conversion turns that value into the set.

`Response<T1..Tn>` is in `Hardened.Requests.Abstract.Responses`. `NotFound` and the other built-in
response types are in `Hardened.Web.Runtime.Responses`. Both namespaces come with the
`Hardened.Web.Runtime` package.

The OpenAPI document lists every case, with its status and its schema. The template's build writes
the document to `src/Todos/openapi/Todos.json`. These are the `responses` of `GET /todos/{id}`:

```json
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
```

The 400 answers a request that fails validation, such as `GET /todos/0`.
[Validation](/guide/validation) covers it.

## Response models

Each handler's return type decides its response model. One class can hold handlers of different
models.

| Model | The handler's return type | A status other than the success | Needs |
|---|---|---|---|
| Response | `Response<T1..Tn>` | Returned, as a case | `net8.0` or later |
| Union | A C# `union` declaration | Returned, as a case | `net11.0` and `<LangVersion>preview</LangVersion>` |
| Throws | The success type | Thrown with `AsException()`, and declared with `[Throws<T>]` | `net8.0` or later |

A Response handler and a Union handler with the same cases send the same responses. The document
lists the same statuses for both. A thrown problem type or generic form sends the same status,
headers and body as the same response returned. [Built-in response types](#built-in-response-types)
describes the problem types and their generic forms.

`dotnet new hardened-web` writes Response handlers. `--response-model union` and
`--response-model throws` write the others. [Project templates](/guide/project-templates) covers the
option.

`[ResponseModel]` on a module changes nothing a code-first build generates. On the class that
carries `[HardenedModule]`, the attribute compiles only with `ResponseModel.Throws`.
`ResponseModel.Response` and `ResponseModel.Union` fail the build with `CS1503`. The error is in the
generated module file, such as the template's `TodosLibrary.Module.g.cs`.

## Cases and statuses

A set takes 2 to 8 type arguments.

A case answers the status in the `[HttpStatus(n)]` on its type. A type without `[HttpStatus]`
answers the success status: 200, or the status the verb attribute's `SuccessStatus` names.
`[HttpStatus]` is in `Hardened.Web.Runtime.Responses`.

A type can be a case once in a set. `Response<Todo, NotFound, NotFound>` fails with `CS0457` where
the handler returns a `NotFound`.

Two cases at one status are one response in the document, with a `oneOf` of the two schemas.

A set that holds no case answers 500 with an empty body. A handler that runs `return default;`
answers that way. So does a handler that returns a null case value.

A handler can return one built-in response type without a set. A handler that returns
`Task<Created<Todo>>` answers 201 with `Location` and the `Todo` as the body. The document lists the
201 with its `Location` header.

## Built-in response types

Each type in this table is a sealed record in `Hardened.Web.Runtime.Responses` that carries
`[HttpStatus]`.

| Type | Status | Constructor | Header it sets |
|---|---|---|---|
| `Ok<T>` | 200 | `(T Value, IReadOnlyDictionary<string, string>? Headers = null)` or `(T value, string headerName, string headerValue)` | The headers it is given |
| `Created<T>` | 201 | `(T Value, string Location)` | `Location` |
| `Accepted` | 202 | `(string? Location = null)` | `Location`, when it is given |
| `NoContent` | 204 | `()` | None |
| `NotModified` | 304 | `(string? ETag = null)` | `ETag`, when it is given |
| `BadRequest` | 400 | `(string? Detail = null)` | None |
| `Unauthorized` | 401 | `(string? Detail = null, AuthorizationChallenge? Challenge = null)` | `WWW-Authenticate`: `Bearer` when no challenge is given |
| `PaymentRequired` | 402 | `(string? Detail = null)` | None |
| `Forbidden` | 403 | `(string? Detail = null)` | None |
| `NotFound` | 404 | `(string Resource, string? Detail = null)` | None |
| `MethodNotAllowed` | 405 | `(string Allow)` | `Allow` |
| `NotAcceptable` | 406 | `()` | None |
| `RequestTimeout` | 408 | `(string? Detail = null)` | None |
| `Conflict` | 409 | `(string? Detail = null)` | None |
| `Gone` | 410 | `(string? Detail = null)` | None |
| `PreconditionFailed` | 412 | `(string? Detail = null)` | None |
| `ContentTooLarge` | 413 | `(string? Detail = null)` | None |
| `UnsupportedMediaType` | 415 | `(string? Detail = null)` | None |
| `UnprocessableContent` | 422 | `(string? Detail = null)` | None |
| `PreconditionRequired` | 428 | `(string? Detail = null)` | None |
| `RateLimited` | 429 | `(TimeSpan RetryAfter, string? Detail = null)` | `Retry-After` |
| `InternalServerError` | 500 | `(string? Detail = null)` | None |
| `NotImplemented` | 501 | `(string? Detail = null)` | None |
| `BadGateway` | 502 | `(string? Detail = null)` | None |
| `ServiceUnavailable` | 503 | `(TimeSpan? After = null, string? Detail = null)` | `Retry-After`, when `After` is given |
| `GatewayTimeout` | 504 | `(string? Detail = null)` | None |

`Ok<T>` and `Created<T>` send `Value` as the body. `NoContent` and `NotModified` send no body.

The types with a `Detail` parameter are the problem types. A problem type sends itself as the body:
its constructor members first, then `type`, `title` and `status`. The 404 at the top of this page
shows that order. `type` is `urn:hardened:problem:` followed by the record's name in lower case with
hyphens. `NotFound` sends `urn:hardened:problem:not-found`. `RateLimited` sends
`urn:hardened:problem:rate-limited`. A `Detail` that is not given is sent as `"detail":null`.

`RateLimited` also sends its wait in the body, as a time span: `"retryAfter":"00:00:30"`.
`ServiceUnavailable` sends `"after"` the same way. `Unauthorized` sends `"challenge"` in its body.
`Retry-After` is the wait in whole seconds, rounded up, and at least 1.

Each problem type except `RateLimited` has a static `Default`: one shared instance with a general
`detail`. `NotFound.Default` sends
`"resource":"resource","detail":"The requested resource does not exist."`.

Each problem type has a generic form, such as `NotFound<T>`. `MethodNotAllowed` has one too. A
generic form answers the same status. It sends a body of the application's own type in place of the
record. The document describes a generic form's case with the schema of its type argument.
`Accepted`, `NoContent`, `NotAcceptable` and `NotModified` have no generic form.

A generic form's constructor is `(T Body)`, except for these:

| Generic form | Constructor |
|---|---|
| `MethodNotAllowed<T>` | `(T Body, string Allow)` |
| `RateLimited<T>` | `(TimeSpan RetryAfter, T Body)` |
| `ServiceUnavailable<T>` | `(T Body, TimeSpan? After = null)` |
| `Unauthorized<T>` | `(T Body, AuthorizationChallenge? Challenge = null)` |

## A status with no built-in type

`Status<TCode, TBody>(TBody Body)` answers the status that `TCode` names. It sends `Body`.

`Http` holds a marker type for each status in this table. It is in `Hardened.Web.Runtime.Responses`.
None of these statuses has a built-in type.

| Statuses | Markers |
|---|---|
| 2xx | `NonAuthoritativeInformation` 203, `ResetContent` 205, `PartialContent` 206, `MultiStatus` 207, `AlreadyReported` 208, `IMUsed` 226 |
| 3xx | `MultipleChoices` 300, `MovedPermanently` 301, `Found` 302, `SeeOther` 303, `UseProxy` 305, `TemporaryRedirect` 307, `PermanentRedirect` 308 |
| 4xx | `ProxyAuthenticationRequired` 407, `LengthRequired` 411, `UriTooLong` 414, `RangeNotSatisfiable` 416, `ExpectationFailed` 417, `ImATeapot` 418, `MisdirectedRequest` 421, `Locked` 423, `FailedDependency` 424, `TooEarly` 425, `UpgradeRequired` 426, `RequestHeaderFieldsTooLarge` 431, `UnavailableForLegalReasons` 451 |
| 5xx | `HttpVersionNotSupported` 505, `VariantAlsoNegotiates` 506, `InsufficientStorage` 507, `LoopDetected` 508, `NotExtended` 510, `NetworkAuthenticationRequired` 511 |

Two statuses with one body type are two different case types. One set can hold both. The set in
`src/Todos/LockController.cs` holds `NotFound<ApiError>` and `Status<Http.Locked, ApiError>`:

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;

namespace Todos;

public record ApiError(string Code, string Message);

[BasePath("/locks")]
public class LockController
{
    [Get("/{id}")]
    public async Task<Response<Todo, NotFound<ApiError>, Status<Http.Locked, ApiError>>> ById(
        ITodoStore store,
        int id
    )
    {
        var todo = await store.Find(id);

        if (todo is null)
        {
            return new NotFound<ApiError>(new ApiError("todo_missing", $"No todo has id {id}."));
        }

        if (!todo.Done)
        {
            return new Status<Http.Locked, ApiError>(
                new ApiError("todo_open", $"Todo {id} is still open.")
            );
        }

        return todo;
    }
}
```

In the template's store, todo 1 is done and todo 2 is not.

```http
GET /todos/locks/2

HTTP/1.1 423 Locked
Content-Type: application/json

{"code":"todo_open","message":"Todo 2 is still open."}
```

```http
GET /todos/locks/99

HTTP/1.1 404 Not Found
Content-Type: application/json

{"code":"todo_missing","message":"No todo has id 99."}
```

The document lists the 404 and the 423 of `GET /todos/locks/{id}` with the `ApiError` schema. The
423's description is `Status 423`.

A marker of the application's own is a struct that implements `IStatusCode`. It carries
`[HttpStatus]` with the number its `Status` returns. `IStatusCode` is in
`Hardened.Requests.Abstract.Responses`. `src/Todos/QuotaExhausted.cs` declares a marker for 466:

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Responses;

namespace Todos;

[HttpStatus(466)]
public readonly struct QuotaExhausted : IStatusCode
{
    public static int Status => 466;
}
```

A case of `Status<QuotaExhausted, ApiError>` answers 466. The document lists a 466 with the
`ApiError` schema.

::: warning
A case whose marker has no `[HttpStatus]` answers the success status, 200, whatever the marker's
`Status` returns. The document adds the case's body to the 200 as a `oneOf`. The build reports
nothing.
:::

## More than one success status

A type of the application's own with `[HttpStatus(n)]` is a case at status n. It sends itself as the
body. A set can hold two success statuses like any other two statuses.

In `src/Todos/ExportController.cs`, `ExportProgress` is a case at 202:

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;

namespace Todos;

public record Export(int Id, string Url);

[HttpStatus(202)]
public record ExportProgress(int Id, int Percent);

[BasePath("/exports")]
public class ExportController
{
    [Get("/{id}")]
    public Response<Export, ExportProgress> ById(int id)
    {
        if (id == 1)
        {
            return new Export(1, "/files/todos-1.json");
        }

        return new ExportProgress(id, 40);
    }
}
```

```http
GET /todos/exports/1

HTTP/1.1 200 OK
Content-Type: application/json

{"id":1,"url":"/files/todos-1.json"}
```

```http
GET /todos/exports/2

HTTP/1.1 202 Accepted
Content-Type: application/json

{"id":2,"percent":40}
```

The document lists the 200 with the `Export` schema and the 202 with the `ExportProgress` schema.

In the Throws model, the return type is the one success. A handler that answers two success statuses
returns a set.

## Response headers

A response type that implements `IProvidesResponseHeaders` sets its headers when it is sent. The
interface is in `Hardened.Requests.Abstract.Responses`. Its one member is
`void ApplyHeaders(IDictionary<string, StringValues> headers)`.

The document lists a header for each `string` parameter of such a type's primary constructor. The
parameter's name is the header's name. `Detail`, `Value` and `Body` are not headers.

| Type | Header | In the document |
|---|---|---|
| `Created<T>` | `Location` | Listed |
| `Accepted` | `Location` | Listed |
| `MethodNotAllowed` | `Allow` | Listed |
| `NotModified` | `ETag` | Listed |
| `Ok<T>` | The headers it is given | Not listed |
| `Unauthorized` | `WWW-Authenticate` | Not listed |
| `RateLimited` and `ServiceUnavailable` | `Retry-After` | Not listed |

`[AnswersHeader(status, name)]` on a handler lists a header on the response with that status.
`Description` sets the header's description. `AnswersHeaderAttribute` is in
`Hardened.Requests.Abstract.Responses`. The attribute adds nothing for a status the operation does
not list.

`src/Todos/VersionedTodoController.cs` sends an `ETag` through `Ok<T>` and lists it with
`[AnswersHeader]`:

```csharp
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;

namespace Todos;

[BasePath("/versioned")]
public class VersionedTodoController
{
    [Get("/{id}")]
    [AnswersHeader(200, "ETag", Description = "The version of the todo.")]
    public async Task<Response<Ok<Todo>, NotFound>> ById(ITodoStore store, int id)
    {
        var todo = await store.Find(id);

        if (todo is null)
        {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return new Ok<Todo>(todo, "ETag", $"\"{todo.Id}\"");
    }
}
```

```http
GET /todos/versioned/1

HTTP/1.1 200 OK
Content-Type: application/json
ETag: "1"

{"id":1,"title":"Read the generated code","done":true}
```

The document lists the `ETag` header on the 200 of `GET /todos/versioned/{id}`:

```json
"200": {
  "description": "OK",
  "headers": {
    "ETag": {
      "description": "The version of the todo.",
      "schema": {
        "type": "string"
      }
    }
  },
  "content": {
    "application/json": {
      "schema": {
        "$ref": "#/components/schemas/Todo"
      }
    }
  }
}
```

## Unions

A C# `union` declaration can take the place of `Response<T1..Tn>`. The handler's body does not
change. The union's cases follow the same rules as a set's: the status from `[HttpStatus]`, and the
same bodies and document entries.

`dotnet new hardened-web -n Todos --response-model union` writes the handler this way:

```csharp
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

public union TodoResult(Todo, NotFound);

public class TodoController
{
    [Operation("getTodo")]
    [Get("/{id}")]
    public async Task<TodoResult> ById(ITodoStore store, [Range(Min = 1)] int id)
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

The union scaffold's library project builds only under the first of these settings:

| Target framework | `LangVersion` | Result |
|---|---|---|
| `net11.0` | `preview` | Builds |
| `net11.0` | Not set | `CS1001`: the `union` declaration does not parse |
| `net11.0` | `15` | `CS1617`: `15` is not a value the compiler accepts |
| `net8.0` or `net10.0` | `preview` | `CS0518` and `CS0656`: `IUnion` and `UnionAttribute` are missing |

The AWS Lambda managed runtime and the Azure Functions worker do not run `net11.0`.
[Project templates](/guide/project-templates) lists the host and model combinations the template
refuses.

## The Throws model

In the Throws model, the return type names the success. Every other status is thrown.
`AsException()` is an extension method in `Hardened.Web.Runtime.Responses`. It turns a built-in
response into a `ResponseException<T>`.

The success answers 200, or the status the verb attribute's `SuccessStatus` names.
[Routing](/guide/routing) covers `SuccessStatus`.

`[Throws<T>]` on the handler lists the status of `T` in the document. Without the attribute, the
document does not list a thrown status.

`dotnet new hardened-web -n Todos --response-model throws` writes the handler this way:

```csharp
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;
using ValidationModules.Constraints;

namespace Todos;

public class TodoController
{
    [Operation("getTodo")]
    [Get("/{id}")]
    [Throws<NotFound>]
    public async Task<Todo> ById(ITodoStore store, [Range(Min = 1)] int id)
    {
        var todo = await store.Find(id);

        if (todo is null)
        {
            throw new NotFound("todo", $"No todo has id {id}.").AsException();
        }

        return todo;
    }
}
```

```http
GET /todos/99

HTTP/1.1 404 Not Found
Content-Type: application/json

{"resource":"todo","detail":"No todo has id 99.","type":"urn:hardened:problem:not-found","title":"Not Found","status":404}
```

The document lists 200, 400 and 404 for `GET /todos/{id}`, as it does for the Response handler at
the top of this page.

`[Throws<T>]` takes the status from the `[HttpStatus]` on `T`. For a type without one, the attribute
states the status: `[Throws<ApiError>(409)]`. An attribute that gives no status for a type without
`[HttpStatus]` fails the build with `HRDT001`. `Description` replaces the response's description in
the document: `[Throws<NotFound>(Description = "No todo has that id.")]`.

The attribute goes on a method. One method can carry several. It does not check what the handler
throws. It changes the document and nothing else, except for
`[Throws<RequestValidationError>(422)]`. That declaration also sets the status of the handler's
validation failures. [Validation](/guide/validation) covers it.

A handler that throws a generic form declares the body type and the status. A handler that throws
`Conflict<ApiError>` declares `[Throws<ApiError>(409)]`. The document then lists the 409 with the
`ApiError` schema.

## What a thrown exception answers

| Thrown | Status | Body |
|---|---|---|
| A problem type or a generic form, through `AsException()` | Its status | Its body, with its headers |
| `ValidationException` | 400 | The validation failure body, which [Validation](/guide/validation) covers |
| `StatusCodeException` | Its status code | `{"type":"StatusCodeException","message":"The todo is locked.","details":""}` for `new StatusCodeException(409, message: "The todo is locked.")` |
| `BadRequestException`, a type derived from it, or `FormatException` | 400 | The exception's type name and its message: `{"type":"BadRequestException","message":"The filter is malformed.","details":""}` for `new BadRequestException("The filter is malformed.")` |
| Any other exception | 500 | `{"type":"ServerError","message":"The server could not complete this request.","details":""}` |

`StatusCodeException` is in `Hardened.Requests.Abstract.Errors`. `BadRequestException` is in
`Hardened.Requests.Runtime.Errors`.

The message of an exception that answers 500 is not sent.

A `FormatException` from `int.Parse("ten")` in a handler answers 400 with its own type name and
message:

```json
{"type":"FormatException","message":"The input string \u0027ten\u0027 was not in a correct format.","details":""}
```

An exception that answers a 4xx status is logged at `Warning` as a refusal:

```text
GET /todos/99 refused with 404: The request produced status 404.
```

An exception that answers any other status is logged at `Error` with its stack trace. A thrown
`ServiceUnavailable` is logged at `Error` too.

## Diagnostics

| Code | Severity | Reported when | Fix |
|---|---|---|---|
| `HRDT001` | Error | `[Throws<T>]` names a type without `[HttpStatus]` and states no status | State the status, as in `[Throws<ApiError>(409)]`, or put `[HttpStatus]` on the type |
| `HRDRM003` | Error | A case is `object` or `dynamic` | Name a specific type |
| `HRDRM004` | Error | Two cases at different statuses, where one type is assignable to the other | Derive both from a base type that is not a case, or give them one status |

`HRDRM004` comes with `CS8120` in the generated handler: "The switch case is unreachable."

## Limits

`Status<TCode, TBody>` sets no headers. A redirect through a marker such as `Http.Found` sends no
`Location`.

A header whose name is not a C# identifier, such as `Retry-After`, cannot be a constructor
parameter. `[AnswersHeader]` lists such a header.

## Next

- [Validation](/guide/validation): the 400 a failed constraint answers, and changing it to 422
- [The OpenAPI document](/guide/openapi-document): the rest of what reaches the document
- [Routing](/guide/routing): `SuccessStatus`, and what a null return answers
- [Typed clients](/guide/testing-clients): asserting a declared response in a test
- [Generating from OpenAPI](/guide/openapi): the interfaces a contract generates

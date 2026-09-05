# Declared responses

A handler that can answer more than one way says so in its return type. `Response<T1..Tn>` is the
whole set, and every status in it reaches the published document.

```csharp
[Get("/todos/{id}")]
public async Task<Response<Todo, NotFound>> ById(ITodoStore store, int id) {
    var todo = await store.Find(id);

    if (todo is null) {
        return new NotFound("todo", $"No todo has id {id}.");
    }

    return todo;
}
```

```json
"responses": {
  "200": { "description": "OK",        "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Todo" } } } },
  "404": { "description": "Not Found", "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Problem" } } } }
}
```

The handler returns the payload and never names the wrapper. The compiler knows the set, the
document describes all of it, and a [generated client](/guide/clients) gets a typed case for the
404.

## The three modes

| | The handler says | Other statuses | Needs |
|---|---|---|---|
| **Response** | the whole set, as `Response<T1..Tn>` | in the return type | any C# compiler |
| **Union** | the whole set, as a C# 15 `union` | in the return type | the .NET 11 SDK |
| **Throws** | one success type | thrown | any C# compiler |

New projects scaffold as `Response`. An application that says nothing is `Throws`. All three work
side by side, and code-first the return type alone decides: write `Response<Todo, NotFound>` and
the handler dispatches over it; write `Todo` and it does not.

## Response

`Response<T1..Tn>` is an ordinary struct with one implicit conversion per case, from two cases to
eight. Because every status is in the signature, the compiler knows the set and the generated
document describes all of it.

A case type may not appear twice. Two identical type arguments produce two identical conversions
and the compiler rejects the use site with `CS0457`, which is why the per-status wrapper types
exist rather than two bare payloads.

### The built-in response types

Every one is a `record` carrying `[HttpStatus]`, so the dispatch reads the status off the type:

| Type | Status | Notes |
|---|---|---|
| `Created<T>(T Value, string Location)` | 201 | sends `Value`, sets `Location` |
| `Accepted(string? Location = null)` | 202 | |
| `NoContent()` | 204 | no body is serialized |
| `NotModified(string? ETag = null)` | 304 | |
| `BadRequest(string? Detail = null)` | 400 | |
| `Unauthorized(string? Detail = null, AuthorizationChallenge? Challenge = null)` | 401 | sets `WWW-Authenticate` |
| `PaymentRequired(string? Detail = null)` | 402 | |
| `Forbidden(string? Detail = null)` | 403 | |
| `NotFound(string Resource, string? Detail = null)` | 404 | |
| `MethodNotAllowed(string Allow)` | 405 | sets `Allow` |
| `NotAcceptable()` | 406 | |
| `RequestTimeout(string? Detail = null)` | 408 | |
| `Conflict(string? Detail = null)` | 409 | |
| `Gone(string? Detail = null)` | 410 | |
| `PreconditionFailed(string? Detail = null)` | 412 | |
| `ContentTooLarge(string? Detail = null)` | 413 | |
| `UnsupportedMediaType(string? Detail = null)` | 415 | |
| `UnprocessableContent(string? Detail = null)` | 422 | |
| `PreconditionRequired(string? Detail = null)` | 428 | |
| `RateLimited(TimeSpan RetryAfter, string? Detail = null)` | 429 | sets `Retry-After` |
| `InternalServerError(string? Detail = null)` | 500 | |
| `NotImplemented(string? Detail = null)` | 501 | |
| `BadGateway(string? Detail = null)` | 502 | |
| `ServiceUnavailable(TimeSpan? After = null, string? Detail = null)` | 503 | sets `Retry-After` |
| `GatewayTimeout(string? Detail = null)` | 504 | |

Each error status has a `<T>` form carrying your own body instead of the default problem shape:
`NotFound<ApiError>(ApiError Body)`, and so on. The wire receives the `Body`, not the wrapper.
`Accepted`, `NoContent`, `NotAcceptable` and `NotModified` carry nothing and have no `<T>` form.

`Created<T>` and the `<T>` forms implement `ICarriesResponseBody`, which is how the dispatch knows
to send the payload rather than the case.

### A status with no record

`Status<TCode, TBody>` covers the registered codes that have no record of their own. The status is
a marker type, and `Http` ships one for every registered code:

```csharp
Response<Pet, Status<Http.ImATeapot, Problem>, Status<Http.Locked, Problem>>
```

Those are two distinct types over one payload schema, so a set declaring both compiles where
`Response<Pet, Problem, Problem>` is `CS0457`. `Status<TCode>` is the bodiless form. An
application declares its own marker for a code nobody registered:

```csharp
public readonly struct QuotaExhausted : IStatusCode {
    public static int Status => 466;
}
```

## Union

The same declared set, as a C# 15 language union:

```csharp
public union TodoResult(Todo, NotFound);

[Get("/todos/{id}")]
public async Task<TodoResult> ById(ITodoStore store, int id) { ... }
```

The handler body is identical to the `Response` version. Hardened matches both structurally, by a
public single-parameter constructor per case and a public `object? Value`, so moving between the
two rewrites no handler. What the keyword adds is exhaustiveness wherever you pattern-match on the
result.

::: warning Union needs .NET 11, not just a language version
The compiler requires `System.Runtime.CompilerServices.IUnion` and `UnionAttribute`, which arrive
with the .NET 11 reference assemblies:

| Target framework | `LangVersion` | Result |
|---|---|---|
| `net8.0` | `preview` | `CS0518`, `CS0656`: `IUnion` and `UnionAttribute` missing |
| `net11.0` | default | `CS1001`: `union` does not parse, because the default is 14.0 |
| `net11.0` | `preview` | builds |

You need both `net11.0` and `<LangVersion>preview</LangVersion>`. On anything less, choose
`Response`. This also rules out AWS Lambda, whose managed runtime is `net8.0`.
:::

Hardened ships no polyfill for `IUnion`, and the runtime never type-tests it.

## Throws

The signature names the success type. Every other status is thrown:

```csharp
[Get("/todos/{id}")]
public async Task<Todo> ById(ITodoStore store, int id) {
    var todo = await store.Find(id);

    if (todo is null) {
        throw new NotFound("todo", $"No todo has id {id}.").AsException();
    }

    return todo;
}
```

`AsException()` wraps any built-in response type in a `ResponseException`, which carries the status
and the body. The 404 on the wire is identical to the one the declared modes return. What differs
is that the signature says nothing about it, so nothing checks that you handled it, and the
document describes only the 200 unless `[Throws<T>]` says otherwise.

One success, and only one. The route attribute names its status, so
`[Post("/todos", SuccessStatus = 201)]` answers 201 without a response set. Returning
`Created<Todo>` in this mode serializes it as an ordinary body at the declared status, not as a
case of its own.

### Declaring what a handler throws

A throw is a statement in a method body, and nothing about the signature says it can happen.
`[Throws<T>]` is the declaration the signature cannot make:

```csharp
[Get("/pets/{petId}")]
[Throws<NotFound>]
[Throws<RateLimited>]
public Task<Pet> GetPet(string petId) { ... }
```

Each declaration becomes a response in the published document, beside the success the return type
already describes. Without it a throws-mode application publishes its 200s and nothing else, and a
client generated from that document has no branch for any failure.

The status comes from the type. `RateLimited` carries `[HttpStatus(429)]`, the same attribute a
`Response<>` or union case is read by, so one vocabulary serves all three modes. A type carrying
no `[HttpStatus]` states the status in the declaration instead: `[Throws<Conflict>(409)]`. A
declaration naming neither is `HRDT001`, an error. `Description` on the attribute overrides the
document's wording for that response.

`[Throws<T>]` promises nothing about the method body. It does not assert that the handler throws
this, nor that it throws nothing else. A handler that wants the compiler checking the set declares
it in the signature instead.

## Choosing a mode

Code-first, the return type decides. Specification-first, the contract declares the statuses and
an MSBuild property decides the shape the interface is generated in:

```xml
<PropertyGroup>
  <HardenedResponseModel>Response</HardenedResponseModel>
</PropertyGroup>
```

`Throws`, `Response` or `Union`. Absent means `Throws`, so an application that has never heard of
this keeps building as it did. The property is where a spec-first project chooses, because the
interface is generated by a build task that runs before the compiler.

`[ResponseModel(ResponseModel.Throws)]` on the module entry point says the same thing for a
code-first module. It has to live there, because an assembly can hold two entry points and neither
the csproj nor an `.editorconfig` can say module A is `Union` and module B is `Throws`.

::: warning `Standard` was renamed in 0.19.0
The mode is `Throws`, named for its mechanism like the other two. `ResponseModel.Standard` remains
as an `[Obsolete]` alias of the same value, so the rename is a warning rather than a break, and it
goes away at 1.0. `[ResponseModel]` compiles on an entry point only with `ResponseModel.Throws`
today.
:::

## Specification-first

The contract is the source of the statuses, so a description declaring a 404 produces a signature
that can answer one whatever mode you are in.

In `Throws`, an operation declaring a 404 generates a nullable return, and `null` is the 404:

```csharp
public Task<Todo?> GetTodo(int id) => _store.Find(id);
```

To explain the refusal instead, throw the response the status resolves to:

```csharp
throw new NotFound<Problem>(new Problem { Detail = $"No todo has id {id}." }).AsException();
```

`Throws` here can answer a non-200 success, because the contract names the status: a `201` in the
description is a 201 on the wire. What it cannot express is more than one success.

In `Response` or `Union`, the build generates a container named `{Operation}Response`. The success
case is the operation's own payload type, and every other status is the shipped record for that
status over the payload the contract declared:

```csharp
public async Task<GetTodoResponse> GetTodo(int id) {
    var todo = await _store.Find(id);

    if (todo is null) {
        return new NotFound("todo", $"No todo has id {id}.");
    }

    return todo;
}
```

A 404 carrying a `Problem` is `NotFound<Problem>`, whatever operation declared it. Two operations
declaring one 404 over one schema share the one type.

The bare record converts. The handler never builds that `Problem`: `new NotFound("todo", "...")`
is the framework's own record, and the build writes the conversion. Where the declared body is RFC
7807 shaped, with `title` and `status` in 7807's types, the set gains an implicit conversion from
the bare record that fills the contract's `Problem` from it. `type`, `title` and `status` come
from what the record knows about its own status and `detail` from what you passed. The same holds
for `Conflict`, `Forbidden` and the rest, and for a Smithy `@error` shape when the record can fill
every member it requires: `message` takes the detail, and the 7807 members fill when the shape
declares `title` and `status`.

What does not convert is anything the record cannot supply: a body with members that are neither
7807's nor defaulted, or an error declaring a header in the contract. Those keep the constructed
form. A status the operation does not declare has no conversion to land on, so returning a
`Conflict` from an operation that declares none is a compile error.

Under `Union` the operator is written in the body of the `union` declaration, so the same handler
compiles against either container. The method the operator calls is generated once, as
`{File}Problems.NotFoundProblem(record)` for an OpenAPI document and
`{Project}Problems.NotFoundTodoNotFound(record)` for a Smithy model.

Every bare record has a shared `Default` with a generic detail, for a handler with nothing to
add. `return NotFound.Default;` allocates nothing in either contract style, because the conversion
hands back one cached case for it.

A `return` hands back the case and the compiler applies the conversion. An implementation with
nothing to await writes `Task.FromResult<GetTodoResponse>(...)` around each return instead.

The success side is unchanged. A success case carries the operation's own payload, so each keeps
a case named `{Operation}{Status}`. An operation declaring `204` and `404` gets
`{Operation}NoContent` and `NotFound<Problem>`.

### When the build still generates a type

Three shapes, and only three.

**An error the description named.** A Smithy `@error` shape, or an OpenAPI `components/responses`
key, keeps that name, once, shared by every operation that binds it:

```csharp
throw new AccountNotFound($"No account {id}.").AsException();
```

`AccountNotFoundException` rather than `GetBalanceBadRequestException` beside
`TransferBadRequestException`. The `AsException()` overload is generated on the payload when the
payload names one error, so the type is written once rather than twice. It lives in `{File}Errors`
in the models namespace, because an extension method has to sit in a non-generic static class.

Two errors over one schema stay written out. `components/responses` lets an author declare
`PetMissing` and `PetLocked` both carrying `ApiError`, and there is no single exception an
`ApiError` means.

**An error declaring a header.** A shipped wrapper has nowhere to put a `Retry-After`, so that
error keeps a case type of its own.

**A status with neither a record nor a marker**, which is only an unregistered code.

## More than one success

An operation declaring two 2xx statuses answers with a response set whatever mode it is in:

```yaml
responses:
  '200': { description: Finished, content: { application/json: { schema: { $ref: '#/components/schemas/Job' } } } }
  '202': { description: Still running, content: { application/json: { schema: { $ref: '#/components/schemas/JobProgress' } } } }
```

`Throws` reaches its other statuses by throwing, and there is no way to throw a 202, so the
signature widens instead. Two schemas at one status are a `oneOf` rather than two cases.

## Diagnostics

| | |
|---|---|
| `HRDT001` | a `[Throws<T>]` names a type with no `[HttpStatus]` and states no status of its own |
| `HRDRM003` | a case is `object` or `dynamic`, so the dispatch would answer that case's status for every response |
| `HRDRM004` | two cases at different statuses where one is assignable to the other, so the document describes two statuses whose schemas overlap |

All are errors rather than warnings. The build compiles and runs either way, and the damage lands
in the shipped contract.

## Next

- [The OpenAPI document](/guide/openapi-document): what each mode publishes
- [Generating from OpenAPI](/guide/openapi): the modes on a generated interface
- [Asserting a response](/guide/testing-responses): the same types in a test

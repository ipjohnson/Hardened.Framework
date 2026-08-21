# Declared responses

A handler that can answer more than one way has to say so somewhere. Hardened gives you three
places to say it, and the choice decides what the compiler checks and what the generated OpenAPI
document describes.

| | The handler says | Other statuses | Compiler floor |
|---|---|---|---|
| **Standard** | one success type | thrown | any |
| **Response** | the whole set, as `Response<T1..Tn>` | in the return type | any |
| **Union** | the whole set, as a C# 15 `union` | in the return type | .NET 11 SDK |

Standard is the default and is not a legacy mode. An application that decides refusals belong in
filters closes the return path deliberately and leaves the throw path open, which is a coherent
policy — so all three keep working side by side.

## Standard

The signature names the success type. Every other status is thrown:

```csharp
[Get("/todos/{id}")]
public Todo ById(ITodoStore store, int id) {
    var todo = store.Find(id);

    if (todo is null) {
        throw new NotFound("todo", $"No todo has id {id}.").AsException();
    }

    return todo;
}
```

`AsException()` wraps any built-in response type in a `ResponseException`, which carries the status
and the body. The 404 a client sees is identical to the one the declared modes return — what differs
is that nothing in the signature says this route can answer it, so nothing checks that you handled
it and the generated document describes only the 200.

**One success, and only one.** A handler in this mode has nowhere to name a status beside its
success type: returning `Created<Todo>` serialises it as an ordinary body at 200, because the
standard dispatch does not read `IHttpStatusResponse` off a returned value. Specification-first is
the exception — see [below](#specification-first).

## Response

The return type is the whole set:

```csharp
[Get("/todos/{id}")]
public Response<Todo, NotFound> ById(ITodoStore store, int id) {
    var todo = store.Find(id);

    if (todo is null) {
        return new NotFound("todo", $"No todo has id {id}.");
    }

    return todo;
}
```

`Response<T1..Tn>` is an ordinary struct with one implicit conversion per case, from two cases to
eight. A handler returns the payload and never names the wrapper. Because every status is in the
signature, the compiler knows the set, and the generated document describes all of them.

It compiles on any C# compiler. Nothing about this mode needs a preview SDK — that is the whole
reason it exists beside `Union`.

**A case type may not appear twice.** Two identical type arguments produce two identical
conversions, and the compiler rejects the use site with `CS0457`. This is why the per-status wrapper
types exist rather than two bare payloads, and it applies under the `union` keyword equally.

### The built-in response types

Every one is a `record` carrying `[HttpStatus]`, so the dispatch reads the status off the type:

| Type | Status | Notes |
|---|---|---|
| `Created<T>(T Value, string Location)` | 201 | sends `Value`, sets `Location` |
| `Accepted(string? Location = null)` | 202 | |
| `NoContent()` | 204 | no body is serialised |
| `Unauthorized(string? Detail = null, AuthorizationChallenge? Challenge = null)` | 401 | sets `WWW-Authenticate` |
| `Forbidden(string? Detail = null)` | 403 | |
| `NotFound(string Resource, string? Detail = null)` | 404 | |
| `Conflict(string? Detail = null)` | 409 | |
| `Gone(string? Detail = null)` | 410 | |
| `PreconditionFailed(string? Detail = null)` | 412 | |
| `RateLimited(TimeSpan RetryAfter, string? Detail = null)` | 429 | sets `Retry-After` |
| `ServiceUnavailable(TimeSpan? After = null, string? Detail = null)` | 503 | sets `Retry-After` |

Each has a `<T>` form that carries your own error body instead of the default problem shape —
`NotFound<ApiError>(ApiError Body)`, and so on. The wire receives the `Body`, not the wrapper, so a
schema written from the wrapper would describe a shape no client ever sees.

`Created<T>` and the `<T>` forms implement `ICarriesResponseBody`, which is how the dispatch knows
to send the payload rather than the case.

## Union

The same declared set, as a C# 15 language union:

```csharp
public union TodoResult(Todo, NotFound);

[Get("/todos/{id}")]
public TodoResult ById(ITodoStore store, int id) { ... }
```

The handler body is identical to the `Response` version. Hardened matches both **structurally** — a
public single-parameter constructor per case and a public `object? Value` — so moving between the
two rewrites no handler and changes no generated dispatch. What the keyword adds is exhaustiveness
wherever you pattern-match on the result.

::: warning Union needs .NET 11, not just a language version
A union is not purely a compiler feature. The compiler requires
`System.Runtime.CompilerServices.IUnion` and `UnionAttribute`, and those arrive with the .NET 11
reference assemblies. Measured on `11.0.100-preview.7`:

| Target framework | `LangVersion` | Result |
|---|---|---|
| `net8.0` | `preview` | `CS0518`, `CS0656` — `IUnion` and `UnionAttribute` missing |
| `net11.0` | default | `CS1001` — `union` does not parse; the default is 14.0 |
| `net11.0` | `preview` | builds |

So you need **both** `net11.0` and `<LangVersion>preview</LangVersion>`. On anything less, choose
`Response`, which declares the same set on any compiler.

This also rules out AWS Lambda, whose managed runtime is `net8.0`.
:::

Hardened ships no polyfill for `IUnion`, and the runtime never type-tests it. A shim that becomes a
real union on an SDK upgrade would change the meaning of already-compiling code with no edit and no
diagnostic.

## Choosing a mode

Code-first, **the return type alone decides**. Write `Response<Todo, NotFound>` and the handler
dispatches over it; write `Todo` and it does not. There is nothing to configure.

Specification-first, the contract declares the statuses and an MSBuild property decides the shape
the interface is generated in:

```xml
<PropertyGroup>
  <HardenedResponseModel>Response</HardenedResponseModel>
</PropertyGroup>
```

`Standard`, `Response` or `Union`; absent means `Standard`. A property rather than an attribute,
because the interface is generated by a build task that runs *before* the compiler — a task that
runs first cannot read an attribute.

## Specification-first

The contract is the source of the statuses, so a description declaring a 404 produces a signature
that can answer one whatever mode you are in.

In `Standard`, an operation declaring a 404 generates a nullable return, and `null` is the 404:

```csharp
public Task<Todo?> GetTodo(int id) => Task.FromResult(_store.Find(id));
```

To explain the refusal instead, throw the generated `{Operation}{Status}Exception`, which carries a
body you wrote.

Standard here **can** answer a non-200 success, because the contract names the status and the
dispatch carries it — a `201` in the description is a 201 on the wire. What it cannot express is
more than one success.

In `Response` or `Union`, the build generates a container named `{Operation}Response` and one case
type per declared status:

```csharp
public Task<GetTodoResponse> GetTodo(int id) {
    var todo = _store.Find(id);

    if (todo is null) {
        return Task.FromResult<GetTodoResponse>(
            new GetTodoNotFound(new Problem { Detail = $"No todo has id {id}." }));
    }

    return Task.FromResult<GetTodoResponse>(todo);
}
```

The success case is the operation's own payload type. Every other status is wrapped in a
`{Operation}{Status}` record whose `Body` is the payload the contract declared — a wrapper is what
carries the status, and it is also what keeps two statuses sharing one schema from putting the same
type in the set twice.

A bodyless status is a case that serialises nothing: an operation declaring `204` and `404` gets a
`{Operation}NoContent` case with no body at all.

## More than one success

An operation declaring two 2xx statuses answers with a response set **whatever mode it is in**:

```yaml
responses:
  '200': { description: Finished, content: { application/json: { schema: { $ref: '#/components/schemas/Job' } } } }
  '202': { description: Still running, content: { application/json: { schema: { $ref: '#/components/schemas/JobProgress' } } } }
```

That is not a preference. `Standard` reaches its other statuses by throwing, and a throw carries a
failure — there is no way to throw a 202. Generating the standard shape here would emit a document
describing a status the handler has no way to produce, so the signature widens instead.

Two schemas at *one* status are a `oneOf` rather than two cases, which the schema layer already
models.

## Diagnostics

| | |
|---|---|
| `HRDRM003` | a case is `object` or `dynamic` — everything is assignable to it, so the dispatch would answer that case's status for every response |
| `HRDRM004` | two cases at different statuses where one is assignable to the other — the document then describes two statuses whose schemas overlap, and a payload validates against both |

Both are errors rather than warnings, because both land in the shipped contract rather than in the
generated code: the switch compiles and runs either way, which is exactly why nothing else would
surface them.

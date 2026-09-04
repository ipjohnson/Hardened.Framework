# Declared responses

A handler that can answer more than one way has to say so somewhere. Hardened gives you three
places, and the choice decides what the compiler checks and what the generated OpenAPI document
describes.

| | The handler says | Other statuses | Compiler floor |
|---|---|---|---|
| **Standard** | one success type | thrown | any |
| **Response** | the whole set, as `Response<T1..Tn>` | in the return type | any |
| **Union** | the whole set, as a C# 15 `union` | in the return type | .NET 11 SDK |

Standard is the default. All three work side by side.

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
and the body. The 404 a client sees is identical to the one the declared modes return; what differs
is that nothing in the signature says this route can answer it, so nothing checks that you handled
it and the generated document describes only the 200. `[Throws<T>]` below is how this mode gets
its failures into the document anyway.

**One success, and only one.** The route attribute names the status that success answers, so
`[Post("/todos", SuccessStatus = 201)]` is a 201 without a response set. What this mode cannot do
is carry a second success case: returning `Created<Todo>` serialises it as an ordinary body at the
declared status, not as a case of its own. Specification-first declares the status in the contract
instead; see [below](#specification-first).

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
already describes. In the second trial's measurement this was the difference between a document
carrying 26 statuses and one carrying 12: without it, a standard-mode application publishes its
200s and nothing else, and a client generated from that document has no branch for any failure.

The status comes from the type. `RateLimited` carries `[HttpStatus(429)]`, which is the same
attribute a `Response<>` or union case is read by, so one vocabulary serves all three modes. A
type carrying no `[HttpStatus]` states the status in the declaration instead:
`[Throws<Conflict>(409)]`. A declaration naming neither is `HRDT001`, an error, because a
response the author wrote down and the document does not carry is worse than one they never
wrote. `Description` on the attribute overrides the document's wording for that response.

`[Throws<T>]` promises nothing about the method body. It does not assert that the handler throws
this, nor that it throws nothing else; an unmapped exception is unplanned by definition and
already has somewhere to land. A handler that wants the compiler checking the set declares it in
the signature with the modes below.

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
signature, the compiler knows the set and the generated document describes all of them. It compiles
on any C# compiler.

**A case type may not appear twice.** Two identical type arguments produce two identical conversions
and the compiler rejects the use site with `CS0457`, which is why the per-status wrapper types exist
rather than two bare payloads.

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

Each has a `<T>` form carrying your own error body instead of the default problem shape —
`NotFound<ApiError>(ApiError Body)`, and so on. The wire receives the `Body`, not the wrapper.

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
two rewrites no handler. What the keyword adds is exhaustiveness wherever you pattern-match on the
result.

::: warning Union needs .NET 11, not just a language version
The compiler requires `System.Runtime.CompilerServices.IUnion` and `UnionAttribute`, which arrive
with the .NET 11 reference assemblies:

| Target framework | `LangVersion` | Result |
|---|---|---|
| `net8.0` | `preview` | `CS0518`, `CS0656` — `IUnion` and `UnionAttribute` missing |
| `net11.0` | default | `CS1001` — `union` does not parse; the default is 14.0 |
| `net11.0` | `preview` | builds |

So you need **both** `net11.0` and `<LangVersion>preview</LangVersion>`. On anything less, choose
`Response`. This also rules out AWS Lambda, whose managed runtime is `net8.0`.
:::

Hardened ships no polyfill for `IUnion`, and the runtime never type-tests it.

## Choosing a mode

Code-first, **the return type alone decides**. Write `Response<Todo, NotFound>` and the handler
dispatches over it; write `Todo` and it does not.

Specification-first, the contract declares the statuses and an MSBuild property decides the shape
the interface is generated in:

```xml
<PropertyGroup>
  <HardenedResponseModel>Response</HardenedResponseModel>
</PropertyGroup>
```

`Standard`, `Response` or `Union`; absent means `Standard`. It is a property rather than an
attribute because the interface is generated by a build task that runs before the compiler.

## Specification-first

The contract is the source of the statuses, so a description declaring a 404 produces a signature
that can answer one whatever mode you are in.

In `Standard`, an operation declaring a 404 generates a nullable return, and `null` is the 404:

```csharp
public Task<Todo?> GetTodo(int id) => Task.FromResult(_store.Find(id));
```

To explain the refusal instead, throw the generated `{Operation}{Status}Exception`, which carries a
body you wrote.

Standard here **can** answer a non-200 success, because the contract names the status — a `201` in
the description is a 201 on the wire. What it cannot express is more than one success.

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
`{Operation}{Status}` record whose `Body` is the payload the contract declared. A bodyless status is
a case that serialises nothing: an operation declaring `204` and `404` gets a `{Operation}NoContent`
case with no body at all.

## More than one success

An operation declaring two 2xx statuses answers with a response set **whatever mode it is in**:

```yaml
responses:
  '200': { description: Finished, content: { application/json: { schema: { $ref: '#/components/schemas/Job' } } } }
  '202': { description: Still running, content: { application/json: { schema: { $ref: '#/components/schemas/JobProgress' } } } }
```

`Standard` reaches its other statuses by throwing, and there is no way to throw a 202, so the
signature widens instead. Two schemas at *one* status are a `oneOf` rather than two cases.

## Diagnostics

| | |
|---|---|
| `HRDT001` | a `[Throws<T>]` names a type with no `[HttpStatus]` and states no status of its own |
| `HRDRM003` | a case is `object` or `dynamic`, so the dispatch would answer that case's status for every response |
| `HRDRM004` | two cases at different statuses where one is assignable to the other, so the document describes two statuses whose schemas overlap |

All are errors rather than warnings: the build compiles and runs either way, and the damage lands
in the shipped contract.

# ![Hardened](https://raw.githubusercontent.com/ipjohnson/Hardened.Framework/main/assets/hardened-mark-32.png) Hardened.Framework

A compile-time, source-generated .NET framework for web APIs and serverless functions. The
dependency injection, routing, parameter binding, configuration and request filters are written by
source generators during the build, not resolved by reflection at startup. What runs is ordinary
C# you can open and read.

The core is provider-agnostic: a handler never learns what host it runs on, and swapping the
runtime module is the whole migration. AWS Lambda is the function compute supported today, through
[Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz).

Full documentation: **[ipjohnson.github.io/Hardened.Docs](https://ipjohnson.github.io/Hardened.Docs)**

## Start here

```bash
dotnet new install Hardened.Templates
dotnet new hardened-web -n Todos
cd Todos
dotnet run --project src/Todos.Host
```

That is a working todo API with tests, on <http://localhost:5080>, with a reference page at
`/docs`.

```console
$ curl localhost:5080/todos/1
{"id":1,"title":"Read the generated code","done":true}
```

Four routes. `GET /todos` has one answer. `GET /todos/{id}`, `POST /todos` and
`DELETE /todos/{id}` each declare more than one. Every example below is from that application.

Start from a template rather than from bare packages. The runtime packages carry no analyzers, so a
project that references only them compiles to an application that answers 404 to everything. The
templates wire the generators, pin every version in one place, and split the projects so the host
can be swapped without touching the code.

| Template | What you get |
|---|---|
| `hardened-web` | The todo API above: an implementation library, a host, and tests. `--host kestrel\|aspnet\|aws-lambda`, `--contract code\|openapi\|smithy`, `--response-model response\|throws\|union` |
| `hardened-function` | A serverless function and tests, on AWS Lambda today. `--trigger invoke\|sqs` |
| `hardened-library` | A reusable module an application picks up with one attribute |

See the [templates guide](https://ipjohnson.github.io/Hardened.Docs/guide/project-templates) for
every option, and [getting started](https://ipjohnson.github.io/Hardened.Docs/guide/getting-started)
for the same project assembled by hand.

## The contract is yours to choose

Hardened builds the same application from any of three contract styles. Pick with
`--contract code|openapi|smithy` on the template, or change your mind later.

### Code-first

The C# is the contract. A route is an attribute on a method of a plain class: no base type, no
interface, no registration. The OpenAPI document is generated *from* your handlers.

```csharp
[HardenedModule]
[HardenedWebModule]
[BasePath("/todos")]               // every route below is relative to this
public partial class TodosLibrary;

public class TodoController {
    [Get("/{id}")]
    public Response<Todo, NotFound> ById(ITodoStore store, int id) {
        var todo = store.Find(id);

        if (todo is null) {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return todo;
    }
}
```

That is the `GET /todos/1` from the quickstart. Services arrive as method parameters, alongside
the route and body values, so anything the container knows about can be asked for that way and
nothing has to be stored on the class. A parameter typed as a concrete class is bound from the
request body instead.

The 404 is in the signature, so the compiler knows the route can answer it and the document
describes it. Naming only the success type and throwing the rest is a mode of its own - the
[three return models](#three-return-models) below are the choice.

The application names its runtime and the libraries it composes, and that is the whole bootstrap:

```csharp
[HardenedModule]
[KestrelRuntime]          // or [AspNetCoreRuntime], or [LambdaWebModule] from Hardened.Amz
[TodosLibrary]
public partial class Application;
```

### OpenAPI-first

An OpenAPI document is the contract. Add it to the project as a `HardenedOpenApiSpec` item and the
build generates the models, a service interface per tag, the routes and the validation its
constraints describe.

```yaml
# contracts/todos.yaml
paths:
  /todos/{id}:
    get:
      tags: [Todos]
      operationId: getTodo
      parameters:
        - { name: id, in: path, required: true, schema: { type: integer, minimum: 1 } }
      responses:
        '200':
          content:
            application/json:
              schema: { $ref: '#/components/schemas/Todo' }
        '404':
          content:
            application/json:
              schema: { $ref: '#/components/schemas/Problem' }
```

You implement the interface it wrote. `[Handler]` is the whole wiring; the verb and the path came
from the document, so neither is restated in C#.

```csharp
[Handler]
public class TodoService : ITodosService {
    private readonly ITodoStore _store;

    public TodoService(ITodoStore store) => _store = store;

    // GetTodoResponse, GetTodoOk and GetTodoNotFound are generated from the two declared
    // statuses, one case each. The set is the return type, so a 404 the contract declares and the
    // handler never returns is a compiler error rather than a document nothing answers to.
    public Task<GetTodoResponse> GetTodo(int id) {
        var todo = _store.Find(id);

        if (todo is null) {
            return Task.FromResult<GetTodoResponse>(
                new GetTodoNotFound(new Problem { Detail = $"No todo has id {id}." }));
        }

        return Task.FromResult<GetTodoResponse>(new GetTodoOk(todo));
    }
}
```

`Todo`, `Problem`, `ITodosService` and the response set are all written by the build, and
`minimum: 1` becomes a validation filter in front of the handler. That shape is the `response`
model, which is what the template scaffolds; the [three return models](#three-return-models) below
are the choice, and in `throws` mode the same operation is a `Task<Todo?>` whose null answers the
declared 404.

There are no route attributes anywhere in the project. Add an operation to the contract and the
build writes the model, the route and the validation, then stops compiling until your service
implements the new method. See
[generating from OpenAPI](https://ipjohnson.github.io/Hardened.Docs/guide/openapi).

### Smithy-first

The same generated output from a [Smithy](https://smithy.io) model instead of an OpenAPI document.

```smithy
service Todos {
    version: "2024-01-01"
    operations: [GetTodo]
}

@error("client")
@httpError(404)
structure TodoNotFound {
    @required
    message: String
}

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

The implementation side is identical. `ITodosService`, `Todo` and the `TodoNotFound` body come
from the model exactly as they came from the document above. `TodoService` is the same class
either way, which is what lets one template generate both.

Constraint traits like `@required` and `@range` become validation filters in front of the handler.
Needs the Smithy CLI on `PATH`; the build names the version it expects if yours differs. See
[generating from Smithy](https://ipjohnson.github.io/Hardened.Docs/guide/smithy).

### Whichever you choose

The application serves its OpenAPI document at `/openapi.json` and a reference page at `/docs`.
Code-first, the document is generated from the routing table. Contract-first, it is generated from
your contract, and an OpenAPI project can serve the source file itself at a second URL, so a client
can read what the build understood or what you wrote. Hardened generates the document; Kiota
generates the client. `<HardenedOpenApiOutput>` writes the served document to a file during the
build, for every contract style and without running the application, and the `hardened-web`
template scaffolds a Kiota C# client from it with a test that drives the client through the
in-process pipeline. The framework's own integration suite does the same over its widest
application. The same file feeds every other generator and language. See
[the OpenAPI document](https://ipjohnson.github.io/Hardened.Docs/guide/openapi-document) and
[clients](https://ipjohnson.github.io/Hardened.Docs/guide/clients).

## Three return models

A handler that can answer more than one way has to say so somewhere. There are three places to say
it. The choice decides what the compiler checks and what the generated document describes, and all
three work side by side.

| | The handler says | Other statuses | Needs |
|---|---|---|---|
| **Response** | the whole set, as `Response<T1..Tn>` | in the return type | any SDK |
| **Throws** | one success type | thrown | any SDK |
| **Union** | the whole set, as a C# `union` | in the return type | .NET 11, `LangVersion` preview |

**Response** is what the template scaffolds, and what the examples above and elsewhere in this
README use: the declared set is where the compiler's checking and the document's truthfulness come
from. It puts the whole set in the return type. `Response<T1..Tn>` is an ordinary struct with an
implicit conversion per case, so the handler returns payloads and never names the wrapper.

```csharp
[Get("/{id}")]
public Response<Todo, NotFound> ById(ITodoStore store, int id) {
    var todo = store.Find(id);

    if (todo is null) {
        return new NotFound("todo", $"No todo has id {id}.");
    }

    return todo;
}
```

**An application that says nothing still gets throws**, and will until 1.0, so nothing built
before 0.19.0 moves when its packages do. `response` is the template's default rather than the
framework's. Code-first there is nothing to set - a handler's return type is the declaration, and
the template writes no attribute. Spec-first the template writes `<HardenedResponseModel>` out for
every mode, so the project file says which one it is rather than leaving you to know the default.

**Throws** names the success type and throws every other status. It was called **standard** until
0.19.0, and it is not a legacy mode - a team that wants errors decided in filters and handlers kept
lean chooses it deliberately. Nothing in the signature says the route can answer a 404, so nothing
checks that you handled it, and the document describes only the 200 unless the handler declares the
rest with `[Throws<NotFound>]` - the attribute the mode is named for.

```csharp
[Get("/{id}")]
[Throws<NotFound>]
public Todo ById(ITodoStore store, int id) {
    var todo = store.Find(id);

    if (todo is null) {
        throw new NotFound("todo", $"No todo has id {id}.").AsException();
    }

    return todo;
}
```

**Union** declares the same set as a C# language union, which adds exhaustiveness wherever you
pattern-match on the result. The handler body is identical to the `Response` version above.

```csharp
public union TodoResult(Todo, NotFound);

[Get("/{id}")]
public TodoResult ById(ITodoStore store, int id) { /* same body */ }
```

Unions need `net11.0` and `<LangVersion>preview</LangVersion>`, which rules out AWS Lambda's
`net8.0` managed runtime today. Hardened matches `Response` and `union` structurally, so moving
between them rewrites no handler. Cases like `NotFound`, `Conflict`, `NoContent` and `Created<T>`
are built-in records that carry their status, and most have a `<T>` form that takes your own body
in place of the default one.

Code-first, the return type alone decides. Contract-first, the statuses come from the contract and
`<HardenedResponseModel>Response|Throws|Union</HardenedResponseModel>` decides the generated
interface's shape. Declared 404s as nullable returns, and operations with two success statuses, are
in [declared responses](https://ipjohnson.github.io/Hardened.Docs/guide/responses).

`--response-model response|throws|union` on the template generates the todo API in whichever of
the three you pick, so the difference between them is something to read rather than to take on
trust. The old value `standard` still scaffolds throws mode, and goes away at 1.0.

## Filters

Every request runs through the same pipeline, whatever the transport: an HTTP call, a function
invocation, a queue message. A pipeline is an ordered list of filters, and the handler you wrote is
the last one. A filter does its work around `chain.Next()`; not calling it short-circuits
everything after it, which is how authorization and caching return without reaching the handler.

```csharp
public class TimingFilter : IExecutionFilter {
    public async Task Execute(IExecutionChain chain) {
        var start = MachineTimestamp.Now;

        try {
            await chain.Next();
        }
        finally {
            chain.Context.RequestMetrics.Record(
                RequestMetrics.TotalRequestDuration, start.GetElapsedMilliseconds());
        }
    }
}
```

Attach a filter to one handler with an attribute (`[Retry]` is the shipped example), or to every
handler through `IGlobalFilterRegistry`. Serialization is itself a filter: the response carries the
handler's return *value*, so a filter that changes the payload changes the value rather than the
bytes. The ordering, the context and the shipped positions are in
[the execution pipeline](https://ipjohnson.github.io/Hardened.Docs/guide/execution-pipeline).

## What else the build writes

The same generate-don't-reflect treatment runs through the rest of the framework:

- **[Parameter binding](https://ipjohnson.github.io/Hardened.Docs/guide/parameter-binding)** —
  path, query, header, body and injected services bind through code emitted for each handler's
  exact signature; a binding that cannot work is a build error.
- **[Configuration](https://ipjohnson.github.io/Hardened.Docs/guide/configuration)** — a
  configuration model is a partial class of private fields; the generator writes the interface,
  the implementation and the environment-variable reads.
- **[Authorization](https://ipjohnson.github.io/Hardened.Docs/guide/authorization)** — a handler
  says what it needs; the pipeline decides whether the caller has it.
- **[Streaming responses](https://ipjohnson.github.io/Hardened.Docs/guide/streaming)** — return
  `IAsyncEnumerable<T>` and the response streams.
- **[Content negotiation](https://ipjohnson.github.io/Hardened.Docs/guide/content-negotiation)**
  and **[System.Text.Json configuration](https://ipjohnson.github.io/Hardened.Docs/guide/json)**
  follow the same shape.

Everything lands as readable source: `EmitCompilerGeneratedFiles` is on in the templates, so the
routing table, the handlers and the binding sit under `obj/<configuration>/<tfm>/generated/`.

## Testing

A test method declares what it needs as parameters. The framework boots the real application around
the test, injects them, and substitutes a mock wherever a parameter is marked `[Mock]`. There is no
socket, port or running host: `ITestWebApp` sends the request through the actual pipeline — routing,
filters, binding, the handler and serialization.

Two assembly attributes are the whole wiring: the harness, and the module under test.

```csharp
[assembly: WebTesting]
[assembly: HardenedTestEntryPoint(typeof(TodosLibrary))]

public class TodoTests {
    [HardenedTest]
    public async Task GetTodo_ReturnsTheTodo(ITestWebApp app) {
        var response = await app.Get("/todos/1");

        response.Assert.Ok();
        Assert.Equal(1, response.Deserialize<TodoResponse>().Id);
    }

    [HardenedTest]
    public async Task GetTodo_UnknownId_IsNotFound(ITestWebApp app) {
        (await app.Get("/todos/9999")).Assert.NotFound();
    }
}
```

A generated client is a parameter too. The harness builds it over an `HttpClient` whose handler is
the pipeline, so the client's calls, its models and its typed exceptions all run against the real
application with no socket. A factory in the test project says how to construct it, once, and
`LastResponse` keeps what the client swallowed:

```csharp
public sealed class TodosClientFactory : ITestClientFactory<TodosClient> {
    public TodosClient Create(HttpClient http) =>
        new(new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: http) {
            BaseUrl = "http://harness"
        });
}

public class TodosClientTests {
    [HardenedTest]
    public async Task CreateTodo_AnswersCreated(TodosClient client) {
        var todo = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" });

        Assert.Equal(201, LastResponse.Status);
        Assert.Equal($"/todos/{todo!.Id}", LastResponse.Headers["Location"]);
    }

    [HardenedTest]
    public async Task UnknownTodo_IsATypedNotFound(TodosClient client) {
        var missing = await Assert.ThrowsAsync<ClientModels.NotFound>(() => client.Todos[9999].GetAsync());

        Assert.Equal(404, missing.ResponseStatusCode);
    }
}
```

Credentials are attributes — `[Grants("todos:write")]`, `[Subject("pia")]`, `[Anonymous]` — on a
parameter, a method, a class or the assembly, and they reach the client as headers. `[Mock]`
composes with a client: the mock sits in the graph the handler resolves from. `GeneratedClientTests`
under `src/IntegrationTests/Web` is the framework's own example, over the application with the
widest route surface it has.

See [testing](https://ipjohnson.github.io/Hardened.Docs/guide/testing),
[testing web apps](https://ipjohnson.github.io/Hardened.Docs/guide/testing-web) and
[clients](https://ipjohnson.github.io/Hardened.Docs/guide/clients).

## Packages

Everything ships to nuget.org as `Hardened.*`, and the templates reference the right set for each
project shape. Assembling by hand, the source generators are not optional and do not flow
transitively: the project that owns the application references them directly. The full list is in
the [package reference](https://ipjohnson.github.io/Hardened.Docs/reference/packages).

## Related repositories

- [Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz) — the AWS provider: Lambda runtimes, test harnesses, DynamoDB client, CDK constructs
- [Hardened.Docs](https://github.com/ipjohnson/Hardened.Docs) — the documentation site

# <picture><source media="(prefers-color-scheme: dark)" srcset="assets/hardened-mark-dark.svg"><img src="assets/hardened-mark.svg" alt="" width="34"></picture> Hardened.Framework

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
dotnet new hardened-web -n Greeter
cd Greeter
dotnet run --project src/Greeter.Host
```

That is a working API with tests, on <http://localhost:5080>, with a reference page at `/docs`.

```console
$ curl localhost:5080/greeting/world
{"message":"Hello, world!"}
```

Start from a template rather than from bare packages: the runtime packages carry no analyzers, so a
project that references only them compiles to an application that answers 404 to everything. The
templates wire the generators, pin every version in one place, and split the projects so the host
can be swapped without touching the code.

| Template | What you get |
|---|---|
| `hardened-web` | An implementation library, a host, and tests. `--host kestrel\|aspnet\|aws-lambda`, `--contract code\|openapi\|smithy` |
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
[BasePath("/greeting")]
public partial class GreeterLibrary;      // the module

public class GreetingController {
    [Get("/{name}")]
    public Greeting Hello(string name) => new($"Hello, {name}!");
}
```

The application names its runtime and the libraries it composes, and that is the whole bootstrap:

```csharp
[HardenedModule]
[KestrelRuntime]          // or [AspNetCoreRuntime], or [LambdaWebModule] from Hardened.Amz
[GreeterLibrary]
public partial class Application;
```

### OpenAPI-first

An OpenAPI document is the contract. Add it to the project as an `AdditionalFiles` item and the
build generates the models, a service interface per tag, the routes and the validation its
constraints describe.

```yaml
# contracts/greeting.yaml
paths:
  /greeting/{name}:
    get:
      tags: [Greeting]
      operationId: hello
      parameters:
        - { name: name, in: path, required: true, schema: { type: string } }
      responses:
        '200':
          content:
            application/json:
              schema: { $ref: '#/components/schemas/Greeting' }
```

You implement the interface it wrote. `[Handler]` is the whole wiring; the verb and the path came
from the document, so neither is restated in C#.

```csharp
[Handler]
public class GreetingService : IGreetingService {
    public Task<Greeting> Hello(string name) =>
        Task.FromResult(new Greeting($"Hello, {name}!"));
}
```

There are no route attributes anywhere in the project. Add an operation to the contract and the
build writes the model, the route and the validation, then stops compiling until your service
implements the new method. The specification and the code cannot disagree.

### Smithy-first

The same generated output from a [Smithy](https://smithy.io) model instead of an OpenAPI document.

```smithy
service Greeter {
    version: "2024-01-01"
    operations: [Hello]
}

@http(method: "GET", uri: "/greeting/{name}")
@readonly
operation Hello {
    input := {
        @httpLabel
        @required
        name: String
    }

    output := {
        @required
        message: String
    }
}
```

The implementation side is identical: implement the generated interface, mark it `[Handler]`.
Constraint traits like `@required` and `@length` become validation filters in front of the handler.
Needs the Smithy CLI on `PATH`; the build names the version it expects if yours differs.

### Whichever you choose

The application serves its OpenAPI document at `/openapi.json` and a reference page at `/docs`.
Code-first, the document is generated from the routing table; contract-first, it is your contract
embedded verbatim. Hardened does not generate clients. The document is the deliverable, and Kiota
or NSwag pointed at it does the rest. See
[the OpenAPI document](https://ipjohnson.github.io/Hardened.Docs/guide/openapi-document) and
[generating from OpenAPI](https://ipjohnson.github.io/Hardened.Docs/guide/openapi).

## Three return models

A handler that can answer more than one way has to say so somewhere. There are three places to say
it. The choice decides what the compiler checks and what the generated document describes, and all
three work side by side.

| | The handler says | Other statuses | Needs |
|---|---|---|---|
| **Standard** | one success type | thrown | any SDK |
| **Response** | the whole set, as `Response<T1..Tn>` | in the return type | any SDK |
| **Union** | the whole set, as a C# `union` | in the return type | .NET 11, `LangVersion` preview |

**Standard** is the default, and not a legacy mode. The signature names the success type and every
other status is thrown. Nothing in the signature says the route can answer a 404, so nothing checks
that you handled it, and the document describes only the 200.

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

**Response** puts the whole set in the return type. `Response<T1..Tn>` is an ordinary struct with
an implicit conversion per case, so the handler returns payloads and never names the wrapper. The
compiler knows the set, the document describes all of it, and it compiles on any SDK.

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

**Union** declares the same set as a C# language union, which adds exhaustiveness wherever you
pattern-match on the result. The handler body is identical to the `Response` version.

```csharp
public union TodoResult(Todo, NotFound);

[Get("/todos/{id}")]
public TodoResult ById(ITodoStore store, int id) { /* same body */ }
```

Unions need `net11.0` and `<LangVersion>preview</LangVersion>`, which rules out AWS Lambda's
`net8.0` managed runtime today. Hardened matches `Response` and `union` structurally, so moving between them
rewrites no handler. Cases like `NotFound`, `Created<T>` and `RateLimited` are built-in records
that carry their status; each has a `<T>` form for your own error body.

The return type alone decides, code-first. Contract-first, the statuses come from the contract and
`<HardenedResponseModel>Standard|Response|Union</HardenedResponseModel>` decides the generated
interface's shape. The full story, including declared 404s as nullable returns and operations with
two success statuses, is in
[declared responses](https://ipjohnson.github.io/Hardened.Docs/guide/responses).

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
handler's return *value*, so a filter that changes the payload changes the value and never needs to
know how it will be written. The ordering, the context and the shipped positions are in
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
socket, port or running host: `ITestWebApp` sends the request through the actual pipeline, meaning
routing, filters, binding, the handler and serialization.

```csharp
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

The pipeline under test is the pipeline that ships. See
[testing](https://ipjohnson.github.io/Hardened.Docs/guide/testing) and
[testing web apps](https://ipjohnson.github.io/Hardened.Docs/guide/testing-web).

## Packages

Everything ships to nuget.org as `Hardened.*`; the templates reference the right set for each
project shape. One rule matters when assembling by hand: the source generators are not optional and
do not flow transitively — the project that owns the application must reference them directly. The
full list, and which project references what, is in the
[package reference](https://ipjohnson.github.io/Hardened.Docs/reference/packages).

## Related repositories

- [Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz) — the AWS provider: Lambda runtimes, test harnesses, DynamoDB client, CDK constructs
- [Hardened.Docs](https://github.com/ipjohnson/Hardened.Docs) — the documentation site

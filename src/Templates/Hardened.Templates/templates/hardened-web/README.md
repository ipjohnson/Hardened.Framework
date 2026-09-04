# Hardened1

A web application on [Hardened](https://github.com/ipjohnson/Hardened.Framework) — a compile-time,
source-generated .NET framework. Routing, request handlers, parameter binding and dependency
injection are written during the build rather than resolved by reflection at run time.

## Run it

```bash
dotnet build
dotnet test
dotnet run --project src/Hardened1.Host
```

It listens on **5080** and prints its address. Set `PORT` to change it.

```bash
curl localhost:5080/todos
[{"id":1,"title":"Read the generated code","done":true},{"id":2,"title":"Add an endpoint","done":false}]
```

Four routes. `GET /todos` has one answer; the other three each declare more than one:

| | success | and |
|---|---|---|
| `GET /todos` | 200 | |
| `GET /todos/{id}` | 200 | 404 when no todo has that id |
#if (throwsMode && codeFirst)
| `POST /todos` | 200 | 409 when the title is taken |
#endif
#if (throwsMode && specFirst)
| `POST /todos` | 201 | 409 when the title is taken |
#endif
#if (declaredMode)
| `POST /todos` | 201 with a `Location` | 409 when the title is taken |
#endif
#if (throwsMode)
| `DELETE /todos/{id}` | 200 | 404 when no todo has that id |
#endif
#if (declaredMode)
| `DELETE /todos/{id}` | 204 | 404 when no todo has that id |
#endif

```bash
curl -i -X POST localhost:5080/todos -H 'Content-Type: application/json' \
     -d '{"title":"Add an endpoint"}'
```

#if (OpenApiUi)
There is a reference page at <http://localhost:5080/docs> and the document behind it at
`/openapi.json`. The page is served in the `development` environment only, and the address is
printed on startup so it does not have to be remembered.

Visual Studio and Rider open it on F5 — pick the **Hardened1.Host (+Browser)** configuration, which
comes from `src/Hardened1.Host/Properties/launchSettings.json`. That profile names no environment
variables on purpose: `dotnet run` applies a profile's variables over the ones already set, so a
`PORT` pinned there would override the caller's. The `dotnet` CLI also ignores `launchBrowser`, so
from a terminal browse to the page yourself.
#endif

#if (kiotaClient)
## The four projects
#else
## The three projects
#endif

| | |
|---|---|
| `src/Hardened1` | Everything the application does — routes, services, models. Knows nothing about where it runs. |
| `src/Hardened1.Host` | Which runtime hosts it, and `Program.cs`. The only host-specific project. |
#if (kiotaClient)
| `src/Hardened1.Client` | The generated client. No hand-written code; Kiota writes it from the document the library's build wrote. |
#endif
| `tests/Hardened1.Tests` | Tests, against the library rather than the host. |

That split is the point rather than a convention. Swapping the host — Kestrel, ASP.NET Core, and
Lambda in the Hardened.Amz packages — changes only the host project. The others are identical
whichever one you pick, which is why the tests target the library: a test suite that named the host
would be tied to a deployment target for no reason.

## Adding to it

#if (codeFirst)
A route is an attribute on a method of a plain class - no base type, no interface, no registration.
`src/Hardened1/TodoController.cs` is the whole pattern:

```csharp
[Get("/{id}")]
public #if (throwsMode)Todo#endif#if (responseMode)Response<Todo, NotFound>#endif#if (unionMode)TodoResult#endif ById(ITodoStore store, int id)
```

`[BasePath]` on `TemplateModuleNameLibrary` prefixes every route in the assembly, so that one is
served at `/todos/{id}`. `[Get]`, `[Post]`, `[Put]`, `[Delete]` and `[Patch]` all behave the same
way.

Services arrive as parameters, and you ask for an interface. A parameter typed as a concrete class
is bound from the request body instead - `HRDR007`, where the class can only be constructed from
services. A service is registered next to the class it belongs to,
with `[SingletonService]`, `[ScopedService]` or `[TransientService]` - the module lists nothing, so
it cannot fall out of step.
#endif
#if (specFirst)
#if (openapi)
The contract is `src/Hardened1/contracts/todos.yaml`.
#endif
#if (smithy)
The contract is `src/Hardened1/contracts/todos.smithy`.

Building needs the [Smithy CLI](https://smithy.io/2.0/guides/smithy-cli/index.html) on `PATH`. The
build names the version it expects if yours differs.
#endif
Add an operation there and the build writes the model, the route, the validation its constraints
describe and the statuses it declares, then stops compiling until `TodoService` implements the new
method.

That is the trade a contract-first project makes: the specification and the code cannot disagree,
because disagreeing is a build error. There are no route attributes anywhere in this project.
#endif

## How responses are declared

#if (throwsMode)
This application is in **throws** mode (named **standard** before 0.19.0). A handler names one
success type and reaches every other status by throwing:

```csharp
throw new NotFound("todo", $"No todo has id {id}.").AsException();
```

The 404 body is the same one the declared modes return. What is missing is any statement in the
signature that the route can answer it - so the generated document describes fewer statuses than
the application actually has, unless the handler declares them with `[Throws<NotFound>]`, which is
this mode's half of the contract and where its name comes from.

#if (codeFirst)
A single success status is nameable — `[Post("/todos", SuccessStatus = 201)]` answers 201 and says
so in the generated document. Creating a todo is left at the default 200 here so the three response
models differ in one thing at a time. What this mode cannot express is more than one success status.
#endif
#if (specFirst)
The contract still names each operation's success status and the dispatch carries it, so creating a
todo answers 201. What this mode cannot express is more than one success status.
#endif

Generate with `--response-model response` to put the whole set in the return type.
#endif
#if (declaredMode)
#if (responseMode)
This application is in **response** mode. A handler returns everything it can answer with:

```csharp
public Response<Todo, NotFound> ById(ITodoStore store, int id) {
    var todo = store.Find(id);

    if (todo is null) {
        return new NotFound("todo", $"No todo has id {id}.");
    }

    return todo;
}
```
#endif
#if (unionMode)
This application is in **union** mode. A handler returns a C# 15 union naming everything it can
answer with:

```csharp
public union TodoResult(Todo, NotFound);

public TodoResult ById(ITodoStore store, int id) { ... }
```

That needs the .NET 11 SDK, pinned in `global.json`, and `LangVersion preview` on the library -
a union needs `IUnion` and `UnionAttribute` from the .NET 11 reference assemblies, not just the
keyword. `--response-model response` gives the same declared set on any compiler.
#endif

One implicit conversion per case, so you return the payload and never name the wrapper. The status
comes from the case, the compiler makes you handle each one, and the generated document describes
all of them - because all of them are in the signature.
#endif

## Testing

`[HardenedTest]` boots the real application — the module graph, configuration and startup services —
and injects what the test asks for. `ITestWebApp` drives the real pipeline without a socket or a
port, so a test exercises routing, filters, binding and serialisation rather than calling a method:

```csharp
[HardenedTest]
public async Task GetTodo_UnknownId_IsNotFound(ITestWebApp app) {
    (await app.Get("/todos/9999")).Assert.NotFound();
}
```

Mark a parameter `[Mock]` and that service is substituted for the whole graph, including behind a
route. Note the argument order on a body: `app.Post(value, path)`.

Every declared status has a test, not only the happy one - a response set exercised only at 200 is
indistinguishable from having none.

#if (kiotaClient)
## Clients

The document is the deliverable. `src/Hardened1/openapi/Hardened1.json` is the OpenAPI document
this service serves, written by the library's build from what the server implements, and
`src/Hardened1.Client` is a C# client Kiota generates from it during the build. The three commands a
consumer needs:

```bash
dotnet build                                          # writes the document, generates and compiles the client
dotnet test                                           # drives the client through the pipeline, no socket
dotnet pack src/Hardened1.Client -p:PackageVersion=1.0.0   # a package whose only dependency is Microsoft.Kiota.Bundle
```

The client is generated into `obj/` and never committed; the document is committed, so a route
change shows in review as a document change. In CI, build and then check the file is current:

```bash
dotnet build
git diff --exit-code src/Hardened1/openapi
```

Two pins move together: the Kiota tool in `.config/dotnet-tools.json` and `KiotaBundleVersion` in
`Directory.Packages.props`. They belong to Kiota's release line, not Hardened's; bump both to one
Kiota release in one commit, and the build says `HTPL003` naming both files if they disagree.

`tests/Hardened1.Tests/Hardened1ClientTests.cs` takes the client as a test parameter, built by the
three-line factory in `TestClients.cs` over the same in-process pipeline `ITestWebApp` drives.
Refusals are asserted with `Assert.ThrowsAsync` in the client's own vocabulary, and the status and
headers the client does not surface are read from `LastResponse`.

Other generators, other languages, and Kiota's multi-language workspace are on the site's
[Clients](https://ipjohnson.github.io/Hardened.Docs/guide/clients) page; every one of them reads the
same file.

#endif
## Reading the generated code

The fastest way to understand any of this is to read what the build wrote. It is ordinary C#, and
`EmitCompilerGeneratedFiles` is already on:

```
src/Hardened1/obj/<configuration>/<tfm>/generated/
```

One directory per generator: the routing table, the handler for each route, the parameter binding
and the module registration are all there.

## Where to go next

- [Documentation](https://ipjohnson.github.io/Hardened.Docs)
- `AGENTS.md` in this directory — the invariants and gotchas, for anyone or anything editing the
  code rather than reading it

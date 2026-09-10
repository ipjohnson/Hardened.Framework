# Hardened1

A web application on [Hardened](https://github.com/ipjohnson/Hardened.Framework) — a compile-time,
source-generated .NET framework. Routing, request handlers, parameter binding and dependency
injection are written during the build rather than resolved by reflection at run time.

## Run it

#if (azureFunctions)
```bash
dotnet build
dotnet test
cd src/Hardened1.Host && func start
```
#else
```bash
dotnet build
dotnet test
dotnet run --project src/Hardened1.Host
```

It listens on **5080** and prints its address. Set `PORT` to change it.
#endif
#if (lambda)

On Lambda there is no web server in the project. Running the host starts the
[AWS Lambda Test Tool](https://github.com/aws/aws-lambda-dotnet/tree/master/Tools/LambdaTestTool-v2)
beside it and points the function at it, so the address above is the tool's API Gateway emulator
and the function runs the way the Lambda service runs it: the same `Main`, bootstrap and event
serialiser, with the debugger attached to it. The tool's own page is at <http://localhost:5050>;
`HARDENED_LAMBDA_EMULATOR_PORT` moves it. The tool is pinned in
`src/Hardened1.Host/.config/dotnet-tools.json` and restored by the build. A deployed function sets
`AWS_LAMBDA_RUNTIME_API`, and then none of this runs.
#endif
#if (cloudRun)

On Cloud Run the same host runs in a container. `PORT` is what Cloud Run sets, and
`CloudRunHost.RunAsync` in `Program.cs` is what drains a request in flight when Cloud Run sends
`SIGTERM`. The `Dockerfile` is the deployment artifact:

```bash
gcloud run deploy hardened1 --source . --region us-central1 --allow-unauthenticated
```

There is no infrastructure package; that command is the deployment.
#endif
#if (azureFunctions)

On Azure Functions there is no web server in the project. The Functions host is the server, and
`func start` from [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
runs it locally, building `src/Hardened1.Host` and starting it as the host's worker.

The host listens on **7071** and serves the routes through the one anonymous HTTP function the
build wrote for them; `host.json` clears the host's `api` route prefix so the paths below are the
paths a deployment answers. `local.settings.json` holds the settings a deployment would put in the
environment. To deploy, create the function app and publish this host into it:

```bash
az group create --name hardened1 --location eastus
az storage account create --name hardened1storage --resource-group hardened1 --sku Standard_LRS
az functionapp create --name hardened1 --resource-group hardened1 --storage-account hardened1storage \
    --consumption-plan-location eastus --runtime dotnet-isolated --functions-version 4
cd src/Hardened1.Host && func azure functionapp publish hardened1
```

There is no infrastructure package; those commands are the deployment.
#endif

#if (azureFunctions)
```bash
curl localhost:7071/todos
#else
```bash
curl localhost:5080/todos
#endif
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
     -d '{"title":"Write a test"}'
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

#if (hasClient)
## The four projects
#else
## The three projects
#endif

| | |
|---|---|
| `src/Hardened1` | Everything the application does — routes, services, models. Knows nothing about where it runs. |
#if (lambda)
| `src/Hardened1.Host` | Which runtime hosts it. `Main` is generated, so there is no `Program.cs`. The only host-specific project. |
#else
| `src/Hardened1.Host` | Which runtime hosts it, and `Program.cs`. The only host-specific project. |
#endif
#if (kiotaClient)
| `src/Hardened1.Client` | The generated client. No hand-written code; Kiota writes it from the document the library's build wrote. |
#endif
#if (refitClient)
| `src/Hardened1.Client` | The generated client. No hand-written code; Refitter writes a Refit interface from the document the library's build wrote. |
#endif
| `tests/Hardened1.Tests` | Tests, against the library rather than the host. |

That split is the point rather than a convention. Swapping the host — Kestrel, ASP.NET Core,
AWS Lambda behind API Gateway, Cloud Run, or Azure Functions — changes only the host project. The others are identical whichever
one you pick, which is why the tests target the library: a test suite that named the host would be
tied to a deployment target for no reason.

## Adding to it

#if (codeFirst)
A route is an attribute on a method of a plain class - no base type, no interface, no registration.
`src/Hardened1/TodoController.cs` is the whole pattern:

#if (throwsMode)
```csharp
[Get("/{id}")]
[Throws<NotFound>]
public async Task<Todo> ById(ITodoStore store, int id)
```
#endif
#if (responseMode)
```csharp
[Get("/{id}")]
public async Task<Response<Todo, NotFound>> ById(ITodoStore store, int id)
```
#endif
#if (unionMode)
```csharp
[Get("/{id}")]
public async Task<TodoResult> ById(ITodoStore store, int id)
```
#endif

`[BasePath]` on `TemplateModuleNameLibrary` prefixes every route in the assembly, so that one is
served at `/todos/{id}`. `[Get]`, `[Post]`, `[Put]`, `[Delete]` and `[Patch]` all behave the same
way.

Services arrive as parameters, and you ask for an interface. A parameter typed as a concrete class
is bound from the request body instead - `HRDR007`, where the class is registered as a service or
can only be constructed from one. A service is registered next to the class it belongs to,
with `[SingletonService]`, `[ScopedService]` or `[TransientService]` - the module lists nothing, so
it cannot fall out of step.
#endif

Every route is in the published document, and `tests/Hardened1.Tests/DocumentStatusTests.cs` holds
the document to what this application answers, operation by operation. Adding a route fails that
test until its `Probes` table carries a request per status the new operation can answer - which is
the point: a status the document declares and nothing answers is the defect a reference page cannot
show.
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
public async Task<Response<Todo, NotFound>> ById(ITodoStore store, int id) {
    var todo = await store.Find(id);

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

public async Task<TodoResult> ById(ITodoStore store, int id) { ... }
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
and injects what the test asks for. There is no socket, port or running host: every request a test
sends goes through the in-process pipeline, so it exercises routing, filters, binding and
serialisation rather than calling a method.

#if (hasClient)
The generated client is a test parameter, and a call through it is asserted with `Returns<T>()`,
naming the response type the contract declares - the status, the body type and the headers that
status carries, in one word. `tests/Hardened1.Tests/TodoTests.cs` drives every operation that way:

```csharp
[HardenedTest]
#if (kiotaClient)
public async Task GetTodo_ReturnsTheTodo(TemplateModuleNameClient client) {
    var todo = await client.Todos[1].GetAsync().Returns<Ok<ClientModels.Todo>>();
#else
public async Task GetTodo_ReturnsTheTodo(ITemplateModuleNameClient client) {
    var todo = await client.GetTodo(1).Returns<Ok<ClientModels.Todo>>();
#endif

#if (xunit)
    Assert.Equal("Read the generated code", todo.Value.Title);
#else
    Assert.That(todo.Value.Title, Is.EqualTo("Read the generated code"));
#endif
}
```

A refusal reads the same way: `Returns<NotFound<...>>()` hands back the body the server answered,
typed as the model the document declares for it, and `ReturnsStatus<T>()` asserts a status the
document declares no body for. `ITestWebApp` sends a raw request through the same pipeline, for
what a typed client cannot send - `(await app.Get("/todos/not-a-number")).Assert.BadRequest()`.
#else
`ITestWebApp` drives the pipeline:

```csharp
[HardenedTest]
public async Task GetTodo_UnknownId_IsNotFound(ITestWebApp app) {
    (await app.Get("/todos/9999")).Assert.NotFound();
}
```
#endif

#if (moq)
Take a `Mock<T>` parameter and that service is substituted for the whole graph, including behind a
route; `tests/Hardened1.Tests/TodoStoreMockTests.cs` does. The mock is Moq's, through
`[assembly: MoqSupport]` in `Bootstrap.cs`. Note the argument order on a body: `app.Post(value, path)`.
#endif
#if (nsubstitute)
Mark a parameter `[Mock]` and that service is substituted for the whole graph, including behind a
route; `tests/Hardened1.Tests/TodoStoreMockTests.cs` does. The substitute is NSubstitute's, through
`[assembly: NSubstituteSupport]` in `Bootstrap.cs`. Note the argument order on a body:
`app.Post(value, path)`.
#endif
#if (fakeiteasy)
Mark a parameter `[Mock]` and that service is substituted for the whole graph, including behind a
route; `tests/Hardened1.Tests/TodoStoreMockTests.cs` does. The fake is FakeItEasy's, through
`[assembly: FakeItEasySupport]` in `Bootstrap.cs`. Note the argument order on a body:
`app.Post(value, path)`.
#endif

Every declared status has a test, not only the happy one - a response set exercised only at 200 is
indistinguishable from having none. `tests/Hardened1.Tests/DocumentStatusTests.cs` is what holds
the suite to that. It sends a request per status, records what came back, and fails when the
published document declares a status nothing answered or the application answers one the document
never mentions. Its table is requests rather than status codes, so a status cannot be claimed
there without a request that produces it.

Each request runs against a container of its own. A todo one request creates is gone by the next,
because the next request built its own container and its own `[SingletonService]` store. That is
the deployment model rather than a harness detail: an execution environment is not promised
between invocations, so a handler leaning on what the last request left behind fails in a test
here rather than intermittently in production.
`tests/Hardened1.Tests/ContainerIsolationTests.cs` shows both sides. Mark a parameter `[Shared]`
to send every request to one container, for a test whose subject is the reuse itself.

#if (hasClient)
## Clients

The document is the deliverable. `src/Hardened1/openapi/Hardened1.json` is the OpenAPI document
this service serves, written by the library's build from what the server implements, and
#if (kiotaClient)
`src/Hardened1.Client` is a C# client Kiota generates from it during the build. The three commands a
consumer needs:

```bash
dotnet build                                          # writes the document, generates and compiles the client
dotnet test                                           # drives the client through the pipeline, no socket
dotnet pack src/Hardened1.Client -p:PackageVersion=1.0.0   # a package whose only dependency is Microsoft.Kiota.Bundle
```
#else
`src/Hardened1.Client` is a Refit interface Refitter generates from it during the build, with the
models it needs. The three commands a consumer needs:

```bash
dotnet build                                          # writes the document, generates and compiles the client
dotnet test                                           # drives the client through the pipeline, no socket
dotnet pack src/Hardened1.Client -p:PackageVersion=1.0.0   # a package whose only dependency is Refit
```
#endif

The client is generated into `obj/` and never committed; the document is committed, so a route
change shows in review as a document change. In CI, build and then check the file is current:

```bash
dotnet build
git diff --exit-code src/Hardened1/openapi
```

#if (kiotaClient)
Two pins move together: the Kiota tool in `.config/dotnet-tools.json` and `KiotaBundleVersion` in
`Directory.Packages.props`. They belong to Kiota's release line, not Hardened's; bump both to one
Kiota release in one commit, and the build says `HTPL003` naming both files if they disagree.

`tests/Hardened1.Tests/TodoTests.cs` takes the client as a test parameter. The
`Hardened.Kiota.Testing` package builds it over the same in-process pipeline `ITestWebApp` drives -
`[assembly: KiotaTesting]` in `Bootstrap.cs` is the whole of the wiring - and each call is asserted
with `Returns<T>()`, naming the response type the contract declares:

```csharp
var created = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" })
    .Returns<Created<ClientModels.Todo>>();

#if (xunit)
Assert.Equal($"/todos/{created.Value.Id}", created.Location);
#else
Assert.That(created.Location, Is.EqualTo($"/todos/{created.Value.Id}"));
#endif

var missing = await client.Todos[9999].GetAsync().Returns<NotFound<ClientModels.Problem>>();

#if (xunit)
Assert.Contains("9999", missing.Body.Detail);
#else
Assert.That(missing.Body.Detail, Does.Contain("9999"));
#endif
```

That is the status, the body type and the headers the status carries in one word, for a success
the client returns and a refusal it throws alike. A Refit interface generated by Refitter is
asserted the same way through `Hardened.Refit.Testing` and `[assembly: RefitTesting]`.
#else
Two pins move together, by hand: the Refitter tool in `.config/dotnet-tools.json` and `Refit` in
`Directory.Packages.props`. They belong to Refit's release line, not Hardened's. Refitter writes
code for Refit's current major and, unlike Kiota, does not report which, so bump both to a matching
pair in one commit and let the build say if they disagree. What Refitter writes is set in
`src/Hardened1.Client/.refitter`: every operation returns an `IApiResponse<T>`, which is the
envelope that carries a status and its headers back beside the body, and the models live in
`Hardened1.Client.Models`.

`tests/Hardened1.Tests/TodoTests.cs` takes the interface as a test parameter. The
`Hardened.Refit.Testing` package builds it over the same in-process pipeline `ITestWebApp` drives -
`[assembly: RefitTesting]` in `Bootstrap.cs` is the whole of the wiring - and each call is asserted
with `Returns<T>()`, naming the response type the contract declares:

```csharp
var created = await client.CreateTodo(new ClientModels.NewTodo { Title = "ship it" })
    .Returns<Created<ClientModels.Todo>>();

#if (xunit)
Assert.Equal($"/todos/{created.Value.Id}", created.Location);
#else
Assert.That(created.Location, Is.EqualTo($"/todos/{created.Value.Id}"));
#endif

#if (codeFirst)
var missing = await client.GetTodo(9999).Returns<NotFound<ClientModels.NotFound>>();
#else
var missing = await client.GetTodo(9999).Returns<NotFound<ClientModels.Problem>>();
#endif

#if (xunit)
Assert.Contains("9999", missing.Body.Detail);
#else
Assert.That(missing.Body.Detail, Does.Contain("9999"));
#endif
```

That is the status, the body type and the headers the status carries in one word, and nothing
throws: Refit hands the whole answer back on the envelope, and a refusal's body is read as the
type the expectation names through the client's own serializer. A Kiota client is asserted the
same way through `Hardened.Kiota.Testing` and `[assembly: KiotaTesting]`.
#endif

Other generators, other languages, and Kiota's multi-language workspace are on the site's
[Clients](https://ipjohnson.github.io/Hardened.Framework/guide/clients) page; every one of them reads the
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

- [Documentation](https://ipjohnson.github.io/Hardened.Framework)
- `AGENTS.md` in this directory — the invariants and gotchas, for anyone or anything editing the
  code rather than reading it
